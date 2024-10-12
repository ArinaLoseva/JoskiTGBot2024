﻿using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using JoskiTGBot2024.Database;
using JoskiTGBot2024.Models;
using JoskiTGBot2024.Services;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

// Псевдоним для устранения конфликта имен с Telegram.Bot.Types.User
using UserModel = JoskiTGBot2024.Models.User;
using Telegram.Bot.Types.ReplyMarkups;

namespace JoskiTGBot2024
{
    public class BotService
    {
        private readonly TelegramBotClient _botClient;
        private readonly ApplicationDbContext _dbContext;
        private readonly ExcelService _excelService;

        public BotService(string token)
        {
            _botClient = new TelegramBotClient(token);
            _dbContext = new ApplicationDbContext();
            _excelService = new ExcelService();
        }

        public void Start()
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { } // Получаем все типы обновлений
            };

            _botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions);
            Console.WriteLine("Бот запущен. Нажмите Enter для завершения.");
            Console.ReadLine();
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message != null)
            {
                var message = update.Message;

                if (message.Text != null && message.Text.StartsWith("/start"))
                {
                    var user = _dbContext.Users.FirstOrDefault(u => u.TelegramUserId == message.Chat.Id);

                    if (user == null || string.IsNullOrEmpty(user.GroupName))
                    {
                        // Пользователь не выбрал группу, показываем кнопку для выбора группы
                        await _botClient.SendTextMessageAsync(message.Chat.Id, "Добро пожаловать! Пожалуйста, выберите свою группу.",
                            replyMarkup: new InlineKeyboardMarkup(
                                InlineKeyboardButton.WithCallbackData("📚 Выбрать группу", "choose_group")
                            ));
                    }
                    else
                    {
                        // Пользователь уже выбрал группу, показываем кнопку для смены группы
                        await _botClient.SendTextMessageAsync(message.Chat.Id, $"Вы уже выбрали группу: {user.GroupName}.",
                            replyMarkup: new InlineKeyboardMarkup(
                                InlineKeyboardButton.WithCallbackData("🔄 Сменить группу", "change_group")
                            ));
                    }
                }

                if (update.CallbackQuery != null)
                {
                    var callbackQuery = update.CallbackQuery;

                    if (callbackQuery.Data == "choose_group")
                    {
                        await _botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Введите название вашей группы (например, П-2109):");
                    }
                    else if (callbackQuery.Data == "change_group")
                    {
                        await _botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Введите новую группу для смены:");
                    }
                }

                // Команда для изменения группы /changegroup
                else if (message.Text?.StartsWith("/changegroup") == true)
                {
                    string newGroupName = message.Text.Split(' ').Length > 1 ? message.Text.Split(' ')[1] : null;
                    if (newGroupName != null)
                    {
                        await ChangeUserGroup(message.Chat.Id, newGroupName);
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(message.Chat.Id, "Пожалуйста, укажите новую группу после команды /changegroup.");
                    }
                }

                // Обработка команды /upload для загрузки таблицы (доступна только администраторам)
                else if (message.Text?.StartsWith("/upload") == true)
                {
                    if (IsAdmin(message.From.Id))
                    {
                        await _botClient.SendTextMessageAsync(message.Chat.Id, "Пожалуйста, отправьте файл Excel с расписанием.");
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(message.Chat.Id, "У вас нет прав для выполнения этой команды.");
                    }
                }

                // Обработка загруженного файла Excel
                else if (message.Document != null)
                {
                    if (IsAdmin(message.From.Id))
                    {
                        await ProcessAdminFile(message.Chat.Id, message.Document.FileId);
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(message.Chat.Id, "У вас нет прав для загрузки файлов.");
                    }
                }

                // Обработка команды /promote для назначения администратора (только для администраторов)
                else if (message.Text?.StartsWith("/promote") == true)
                {
                    if (IsAdmin(message.From.Id))
                    {
                        string[] commandParts = message.Text.Split(' ');
                        if (commandParts.Length > 1 && long.TryParse(commandParts[1], out long promoteUserId))
                        {
                            await PromoteToAdmin(promoteUserId);
                        }
                        else
                        {
                            await _botClient.SendTextMessageAsync(message.Chat.Id, "Используйте команду: /promote <TelegramUserId>");
                        }
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(message.Chat.Id, "У вас нет прав для выполнения этой команды.");
                    }
                }
            }
        }

        private async Task ChangeUserGroup(long chatId, string newGroupName)
        {
            var user = _dbContext.Users.FirstOrDefault(u => u.TelegramUserId == chatId);
            if (user != null)
            {
                user.GroupName = newGroupName;
                await _dbContext.SaveChangesAsync();
                await _botClient.SendTextMessageAsync(chatId, $"Ваша группа была успешно изменена на {newGroupName}");
            }
            else
            {
                await _botClient.SendTextMessageAsync(chatId, "Вы не зарегистрированы. Пожалуйста, используйте команду /start для регистрации.");
            }
        }



        // Метод для регистрации пользователя
        private async Task RegisterUser(long chatId, string groupName)
        {
            var user = _dbContext.Users.FirstOrDefault(u => u.TelegramUserId == chatId);
            if (user == null)
            {
                var newUser = new UserModel { TelegramUserId = chatId, GroupName = groupName, IsAdmin = false };
                _dbContext.Users.Add(newUser);
                await _dbContext.SaveChangesAsync();
            }
            Console.WriteLine("новый чел " + chatId);
            foreach (var item in _dbContext.Users)
            {
                Console.WriteLine(item.TelegramUserId + " " + item.IsAdmin);
            }
            await _botClient.SendTextMessageAsync(chatId, $"Вы зарегистрированы в группе {groupName}");
        }

        // Метод для назначения администратора
        private async Task PromoteToAdmin(long userId)
        {
            var user = _dbContext.Users.FirstOrDefault(u => u.TelegramUserId == userId);
            if (user != null && !user.IsAdmin)
            {
                user.IsAdmin = true;
                await _dbContext.SaveChangesAsync();
                await _botClient.SendTextMessageAsync(userId, "Вы были назначены администратором.");
            }
            else
            {
                await _botClient.SendTextMessageAsync(userId, "Пользователь уже является администратором или не зарегистрирован.");
            }
        }

        // Метод для проверки прав администратора
        private bool IsAdmin(long userId)
        {
            var user = _dbContext.Users.FirstOrDefault(u => u.TelegramUserId == userId);
            return user != null && user.IsAdmin;
        }

        // Метод для обработки загруженного файла администратора
        private async Task ProcessAdminFile(long adminId, string fileId)
        {
            var file = await _botClient.GetFileAsync(fileId);
            var fileStream = new MemoryStream();
            await _botClient.DownloadFileAsync(file.FilePath, fileStream);

            var schedule = _excelService.ProcessExcelFile(fileStream);
            var scheduleService = new ScheduleService(schedule);

            var users = _dbContext.Users.ToList();
            foreach (var user in users)
            {
                var scheduleMessage = scheduleService.GetScheduleForGroup(user.GroupName);
                await _botClient.SendTextMessageAsync(user.TelegramUserId, scheduleMessage);
            }
        }

        // Метод для обработки ошибок
        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Ошибка: {exception.Message}");
            return Task.CompletedTask;
        }
    }
}
