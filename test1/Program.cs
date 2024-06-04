using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using test1.Models;

public class Program
{
    public static void Main(string[] args)
    {
        telegram_bot bot = new telegram_bot();
        bot.InitializeBot();
        Console.ReadLine();
    }
}
public class telegram_bot
{
    private int i;
    private readonly IMemoryCache _cache;
    private readonly HttpClient httpClient = new HttpClient();
    Dictionary<long, (int start_year, int end_year, int min_imdb, int max_imdb)> chatParameters = new Dictionary<long, (int, int, int, int)>();
    private ITelegramBotClient? botClient = null;
    private static string? _botApiKey = test1.Constants.TelegramBotApiKey;
    private static string? _address = test1.Constants.Address;

    public telegram_bot()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    ReceiverOptions receiverOptions = new()
    {
        AllowedUpdates = Array.Empty<UpdateType>()
    };

    public async void InitializeBot()
    {
        botClient = new TelegramBotClient(_botApiKey);
        botClient.StartReceiving(updateHandler: HandleUpdateAsync, pollingErrorHandler: HandlePollingErrorAsync, receiverOptions: receiverOptions);
        var me = await botClient.GetMeAsync();
        Console.WriteLine($"Bot {me.Username} started working");
        Console.ReadLine();
    }
    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken token)
    {
        if (update.Type == UpdateType.Message && update?.Message?.Text != null)
        {
            await HandlerMessageAsync(botClient, update.Message);
        }
    }
    private Task HandlePollingErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken token)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"Error in telegram bot API:\n {apiRequestException.ErrorCode}\n {apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(errorMessage);
        return Task.CompletedTask;
    }
    private async Task HandlerMessageAsync(ITelegramBotClient botClient, Message message)
    {
        if (message.Text == "/start")
        {
            await botClient.SendTextMessageAsync(
                chatId: message.Chat,
                text: "Привіт! Я бот, який допоможе тобі знайти фільми, які ти зможеш додати до списку \"To Watch\"."
            );

            ReplyKeyboardMarkup replyKeyboardMarkup = new
            (
                new[]
                {
                        new KeyboardButton[] { "Пошук фільмів", "Список \"To Watch\"" },
                }
            )
            {
                ResizeKeyboard = true
            };
            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                text: " Щоб почати, обери в меню функцію:",
                replyMarkup: replyKeyboardMarkup
            );
            return;
        }
        else if (message.Text == "/keyboard")
        {
            ReplyKeyboardMarkup replyKeyboardMarkup = new
            (
                new[]
                {
                        new KeyboardButton[] { "Пошук фільмів", "Список \"To Watch\"" },
                }
            )
            {
                ResizeKeyboard = true
            };
            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                text: " Щоб почати, обери в меню функцію:",
                replyMarkup: replyKeyboardMarkup
            );
            return;
        }
        //else if (message.Text == "/inline")
        //{
        //    InlineKeyboardMarkup keyboardMarkup = new
        //    (
        //        new[]
        //        {
        //            new[]
        //            {
        //                InlineKeyboardButton.WithCallbackData("курс долара", $"currencyUSD"),
        //                InlineKeyboardButton.WithCallbackData("курс євро", $"currencyEUR")
        //            }
        //        }
        //    );
        //    await botClient.SendTextMessageAsync(message.Chat.Id, "оберіть валюту", replyMarkup: keyboardMarkup);
        //    return;
        //}

        if (message.Text.Equals("Пошук фільмів"))
        {
            await botClient.SendTextMessageAsync(
                chatId: message.Chat,
                text: "Напиши початковий рік випуску фільму, кінцевий рік випуску фільму, мінімальний рейтинг imdb та максимальний рейтинг imdb в форматі: /searchlist **** **** * *. Наприклад, /searchlist 2016 2017 6 8"
            );
            return;
        }
        else if (message.Text.StartsWith("/searchlist"))
        {
            var movieClient = new MovieList();
            if (message.Type == MessageType.Text)
            {
                var parts = message.Text.Split(' ');
                if (parts.Length == 5
                    && int.TryParse(parts[1], out int start_year)
                    && int.TryParse(parts[2], out int end_year)
                    && int.TryParse(parts[3], out int min_imdb)
                    && int.TryParse(parts[4], out int max_imdb))
                {
                    chatParameters[message.Chat.Id] = (start_year, end_year, min_imdb, max_imdb);
                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat,
                        text: $"Параметри встановлені на: початковий рік {start_year}, кінцевий рік {end_year}, мінімальний рейтинг IMDB {min_imdb}, максимальний рейтинг IMDB {max_imdb}."
                    );
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat,
                        text: "Введіть команду у форматі: /searchlist start_year end_year min_imdb max_imdb. Наприклад, /searchlist 2016 2017 6 8"
                    );
                    return;
                }
            }
            var parameters = chatParameters[message.Chat.Id];
            string responseBody = await SearchMovie(parameters.start_year, parameters.end_year, parameters.min_imdb, parameters.max_imdb, message.Chat.Id);
            MovieList movieList = JsonConvert.DeserializeObject<MovieList>(responseBody);
            if (movieList != null && movieList.results.Length != 0)
            {
                _cache.Set("MovieList", movieList);
                var titles = movieList.results.Select(m => m.title).ToList();
                var titlesString = string.Join("\n", titles);

                await botClient.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: titlesString
                );
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "Щоб отримати інформацію про фільм, введіть /getmovie Назва фільму. Наприклад, /getmovie The Shawshank Redemption."
                );
                return;
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "Фільмів за вашим запитом не знайдено."
                );
            }
        }
        else if (message.Text.StartsWith("/getmovie") && message.Text.Length > 9)
        {
            var title = message.Text.Substring(10);
            string responseBody1 = await SearchMovieByName(title, message.Chat.Id);
            MovieByName movieList1 = JsonConvert.DeserializeObject<MovieByName>(responseBody1);
            if (movieList1 != null)
            {
                await botClient.SendPhotoAsync(
                    chatId: message.Chat,
                    photo: InputFile.FromUri(movieList1.imageurl[0]),
                    caption: $"Назва: {movieList1.title}\nЖанр: {string.Join(", ", movieList1.genre)}\nРік випуску: {movieList1.released}\nРейтинг IMDB: {movieList1.imdbrating}\nСинопсис: {movieList1.synopsis}",
                    parseMode: ParseMode.Html
                );
                return;
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "Фільмів за вашим запитом не знайдено."
                );
            }
            return;
        }
        else
        {
            await botClient.SendTextMessageAsync(
                 chatId: message.Chat,
                 text: "Невірна команда. Спробуй ще раз! \n/keyboard"
             );
        }
    }
    private async Task<string> SearchMovie(int start_year, int end_year, int min_imdb, int max_imdb, long id)
    {
        try
        {
            HttpResponseMessage response = await httpClient.GetAsync(_address + $"/controller/GetMovieListIMDBRating?start_year={start_year}&end_year={end_year}&min_imdb={min_imdb}&max_imdb={max_imdb}");
            Console.WriteLine($"ID: {id}: -- Response status code: {response.StatusCode}");
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody;
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"ID: {id}: -- Request error: {e.Message}");
            return "Error fetching rates";
        }
        catch (Exception e)
        {
            Console.WriteLine($"ID: {id}: -- Unexpected error: {e.Message}");
            return "Error fetching rates";
        }
    }
    private async Task<string> SearchMovieByName(string obj, long id)
    {
        try
        {
            string title = Uri.EscapeDataString(obj);
            HttpResponseMessage response = await httpClient.GetAsync(_address + $"/controller/GetMovieByTitle?title={title}");
            Console.WriteLine($"ID: {id}: -- Response status code: {response.StatusCode}");
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody;
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"ID: {id}: -- Request error: {e.Message}");
            return "Error fetching rates";
        }
        catch (Exception e)
        {
            Console.WriteLine($"ID: {id}: -- Unexpected error: {e.Message}");
            return "Error fetching rates";
        }
    }
}