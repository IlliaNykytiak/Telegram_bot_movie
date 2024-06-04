using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram_Bot.Models;
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
        if (update == null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        if (botClient == null)
        {
            throw new ArgumentNullException(nameof(botClient));
        }

        if (update.Type == UpdateType.Message)
        {
            await HandlerMessageAsync(botClient, update.Message);
        }
        else if (update.Type == UpdateType.CallbackQuery)
        {
            await HandlerCallbackQueryAsync(botClient, update.CallbackQuery);
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
    private async Task HandlerCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery)
    {
        if (callbackQuery == null)
        {
            throw new ArgumentNullException(nameof(callbackQuery));
        }
        if (callbackQuery.Data == "addtofavoriteslist")
        {
            var movieInfo = _cache.Get<MovieByName>("ByTitle");
            Console.WriteLine(await AddDB((int)callbackQuery.From.Id, movieInfo));
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, text: "Фільм додано до списку обраних.");
            return;
        }
        else if (callbackQuery.Data == "updateinfo")
        {
            var movieInfo = _cache.Get<MovieByName>("ByTitle");
            Console.WriteLine(await UpdateDB((int)callbackQuery.From.Id, movieInfo));
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, text: "Інформація оновлена.");
        }
        else if (callbackQuery.Data == "deletefromtowatchlist")
        {
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, text: "Використовуйте інструкцію");
            await botClient.SendTextMessageAsync(
                chatId: callbackQuery.Message.Chat,
                text: "Напишіть назву фільму, який ви хочете видалити зі списку \"To Watch\" в форматі: -Name. \nНаприклад: -The Shawshank Redemption"
            );
            return;
        }
        else if (callbackQuery.Data == "cleartowatchlist")
        {
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, text: "Використовуйте інструкцію");
            await botClient.SendTextMessageAsync(
                chatId: callbackQuery.Message.Chat,
                text: "Ви впевнені, що хочете очистити список \"To Watch\"? \nТак/Ні"
            );
            return;
        }
        else
        {
            await botClient.SendTextMessageAsync(
                chatId: callbackQuery.Message.Chat,
                text: "Невірна команда. Спробуй ще раз! \n/keyboard"
            );
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, text: "Невірна команда.");
        }
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

        if (message.Text.Equals("Пошук фільмів"))
        {
            await botClient.SendTextMessageAsync(
                chatId: message.Chat,
                text: "Напиши початковий рік випуску фільму, кінцевий рік випуску фільму, мінімальний рейтинг imdb та максимальний рейтинг imdb в форматі: /searchlist **** **** * *. Наприклад, /searchlist 2016 2017 6 8"
            );
            return;
        }
        else if (message.Text.Equals("Список \"To Watch\""))
        {
            InlineKeyboardMarkup keyboardMarkup2 = new
            (
                new[]
                {
                                    new[]
                                    {
                                        InlineKeyboardButton.WithCallbackData("Видалити зі списку", $"deletefromtowatchlist"),
                                    },
                                    new[]
                                    {
                                        InlineKeyboardButton.WithCallbackData("Очистити список", $"cleartowatchlist")
                                    },
                }
            );
            await botClient.SendTextMessageAsync(
                chatId: message.Chat,
                text: await ToWatchList((int)message.Chat.Id),
                replyMarkup: keyboardMarkup2
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
            _cache.Set("ByTitle", movieList1);
            if (movieList1 != null)
            {
                InlineKeyboardMarkup keyboardMarkup = new
                (
                    new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithUrl("IMDB", $"https://www.imdb.com/title/{movieList1.imdbid}"),
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("Додати до списку обраних", $"addtofavoriteslist"),
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("Оновити інформацію", $"updateinfo")
                        },
                    }
                );
                await botClient.SendPhotoAsync(
                    chatId: message.Chat.Id,
                    photo: InputFile.FromUri(movieList1.imageurl[0]),
                    caption: $"Назва: {movieList1.title}\nЖанр: {string.Join(", ", movieList1.genre)}\nРік випуску: {movieList1.released}\nРейтинг IMDB: {movieList1.imdbrating}\nСинопсис: {movieList1.synopsis}",
                    parseMode: ParseMode.Html,
                    replyMarkup: keyboardMarkup
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
        else if (message.Text.StartsWith("-"))
        {
            var title = message.Text.Substring(1);
            await DeleteFromDB((int)message.Chat.Id, title);
            await botClient.SendTextMessageAsync(
                chatId: message.Chat,
                text: "Фільм видалено зі списку \"To Watch\"."
            );
            InlineKeyboardMarkup keyboardMarkup2 = new
            (
                new[]
                {
                                                new[]
                                                {
                                                    InlineKeyboardButton.WithCallbackData("Видалити зі списку", $"deletefromtowatchlist"),
                                                },
                                                new[]
                                                {
                                                    InlineKeyboardButton.WithCallbackData("Очистити список", $"cleartowatchlist")
                                                },
                }
            );
            await botClient.SendTextMessageAsync(
                chatId: message.Chat,
                text: await ToWatchList((int)message.Chat.Id),
                replyMarkup: keyboardMarkup2
            );
            return;
        }
        else if (message.Text.ToLower() == "так")
        {
            await ClearToWatch((int)message.Chat.Id);
            await botClient.SendTextMessageAsync(
                chatId: message.Chat,
                text: "Список \"To Watch\" очищено."
            );
            return;
        }
        else if (message.Text.ToLower() == "ні")
        {
            await botClient.SendTextMessageAsync(
                chatId: message.Chat,
                text: "Список \"To Watch\" не очищено."
            );
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
    private async Task<string> AddDB(int ID, MovieByName movieInfo)
    {
        try
        {
            var movieInfoJson = new StringContent(
                JsonConvert.SerializeObject(movieInfo),
                Encoding.UTF8,
                "application/json");

            HttpResponseMessage response = await httpClient.PostAsync(_address + $"/controller/AddMovie?ID={ID}", movieInfoJson);
            Console.WriteLine($"ID: {ID}: -- Response status code: {response.StatusCode}");
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody;
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"ID: {ID}: -- Request error: {e.Message}");
            return "Error fetching rates";
        }
        catch (Exception e)
        {
            Console.WriteLine($"ID: {ID}: -- Unexpected error: {e.Message}");
            return "Error fetching rates";
        }
    }
    private async Task<string> UpdateDB(int ID, MovieByName movieInfo)
    {
        try
        {
            var movieInfoJson = new StringContent(
                JsonConvert.SerializeObject(movieInfo),
                Encoding.UTF8,
                "application/json");

            HttpResponseMessage response = await httpClient.PutAsync(_address + $"/controller/UpdateMovie/{ID}", movieInfoJson);
            Console.WriteLine($"ID: {ID}: -- Response status code: {response.StatusCode}");
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody;
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"ID: {ID}: -- Request error: {e.Message}");
            return "Error fetching rates";
        }
        catch (Exception e)
        {
            Console.WriteLine($"ID: {ID}: -- Unexpected error: {e.Message}");
            return "Error fetching rates";
        }
    }
    private async Task<string> DeleteFromDB(int ID, string title)
    {
        try
        {
            HttpResponseMessage response = await httpClient.DeleteAsync(_address + $"/controller/DeleteMovie/{ID}?title={title}");
            Console.WriteLine($"ID: {ID}: -- Response status code: {response.StatusCode}");
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody;
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"ID: {ID}: -- Request error: {e.Message}");
            return "Error fetching rates";
        }
        catch (Exception e)
        {
            Console.WriteLine($"ID: {ID}: -- Unexpected error: {e.Message}");
            return "Error fetching rates";
        }
    }
    private async Task<string> ToWatchList(int ID)
    {
        try
        {
            HttpResponseMessage response = await httpClient.GetAsync(_address + $"/controller/GetAllToWatch?chat_ID={ID}");
            Console.WriteLine($"ID: {ID}: -- Response status code: {response.StatusCode}");
            string responseBody = await response.Content.ReadAsStringAsync();
            var movies = JsonConvert.DeserializeObject<List<ToWatchList>>(responseBody);

            if (movies.Count > 0)
            {
                var sb = new StringBuilder();

                for (int i = 0; i < movies.Count; i++)
                {
                    sb.AppendLine($"{i + 1}. {movies[i].title}");
                }

                string message = sb.ToString();
                return message;
                // Send the message to the bot
            }
            else
            {
                return "Список \"To Watch\" порожній.";
            }
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"ID: {ID}: -- Request error: {e.Message}");
            return "Error fetching rates";
        }
        catch (Exception e)
        {
            Console.WriteLine($"ID: {ID}: -- Unexpected error: {e.Message}");
            return "Error fetching rates";
        }
    }
    private async Task<string> ClearToWatch(int ID)
    {
        try
        {
            HttpResponseMessage response = await httpClient.DeleteAsync(_address + $"/controller/ClearToWatchList/{ID}");
            Console.WriteLine($"ID: {ID}: -- Response status code: {response.StatusCode}");
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody;
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"ID: {ID}: -- Request error: {e.Message}");
            return "Error fetching rates";
        }
        catch (Exception e)
        {
            Console.WriteLine($"ID: {ID}: -- Unexpected error: {e.Message}");
            return "Error fetching rates";
        }
    }
}