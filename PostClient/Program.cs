using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http.Headers;

namespace ConsoleBlogClient
{
	class Program
	{
		private static readonly HttpClient _client = new HttpClient();
		private static string _token = string.Empty; // Храним текущий токен

		static async Task Main(string[] args)
		{
			
			_client.BaseAddress = new Uri("http://192.168.0.25:5210");

			while (true)
			{
				Console.WriteLine();

				// Если пользователь НЕ авторизован, показываем только register/login
				if (string.IsNullOrEmpty(_token))
				{
					Console.WriteLine("=== Вы НЕ авторизованы! Доступные команды: ===");
					Console.WriteLine("1) register  - регистрация");
					Console.WriteLine("2) login     - вход (получить токен)");
					Console.WriteLine("0) exit      - выход");
				}
				else
				{
					// Пользователь авторизован => всё меню
					Console.WriteLine("=== Вы АВТОРИЗОВАНЫ! Доступные команды: ===");
					Console.WriteLine("1) create_post      - создать новый пост (title + content)");
					Console.WriteLine("2) delete_post      - удалить пост по ID");
					Console.WriteLine("3) update_post      - обновить существующий пост (PATCH)");
					Console.WriteLine("4) get_all_posts    - посмотреть все посты (GET /posts)");
					Console.WriteLine("5) get_post         - посмотреть один пост по ID (GET /posts/{id})");
					Console.WriteLine("6) logout           - выйти из учётной записи (очистить токен)");
					Console.WriteLine("0) exit            - завершить программу");
				}

				Console.Write(">> ");
				var command = Console.ReadLine()?.ToLower().Trim();
				if (command == "exit" || command == "0") break;

				try
				{
					// Логика: если нет токена, то работаем только с register/login
					if (string.IsNullOrEmpty(_token))
					{
						switch (command)
						{
							case "1":
							case "register":
								await RegisterAsync();
								break;
							case "2":
							case "login":
								await LoginAsync();
								break;
							default:
								Console.WriteLine("Неизвестная команда (нужно сначала зарегистрироваться / авторизоваться).");
								break;
						}
					}
					else
					{
						// Когда пользователь авторизован (есть _token), даём доступ к CRUD
						switch (command)
						{
							case "1":
							case "create_post":
								await CreatePostAsync();
								break;
							case "2":
							case "delete_post":
								await DeletePostAsync();
								break;
							case "3":
							case "update_post":
								await UpdatePostAsync();
								break;
							case "4":
							case "get_all_posts":
								await GetAllPostsAsync();
								break;
							case "5":
							case "get_post":
								await GetPostByIdAsync();
								break;
							case "6":
							case "logout":
								Logout();
								break;
							default:
								Console.WriteLine("Неизвестная команда.");
								break;
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine("Ошибка: " + ex.Message);
				}
			}

			Console.WriteLine("Программа завершена.");
		}

		// ========================================================
		//     РЕГИСТРАЦИЯ / ЛОГИН / ЛОГАУТ
		// ========================================================

		private static async Task RegisterAsync()
		{
			Console.Write("Введите имя пользователя (username): ");
			string username = Console.ReadLine() ?? "";

			Console.Write("Введите пароль (password): ");
			string password = Console.ReadLine() ?? "";

			// Формируем URL: POST /register?username=...&password=...
			string url = $"/register?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}";

			var response = await _client.PostAsync(url, content: null);

			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync();
				// Ожидаем { message="", token="" }
				var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

				if (dict != null && dict.TryGetValue("token", out var newToken))
				{
					_token = newToken;
					Console.WriteLine("Регистрация прошла успешно! Ваш токен: " + _token);
				}
				else
				{
					Console.WriteLine("Регистрация успешна, но не получили токен.");
				}
			}
			else
			{
				Console.WriteLine("Ошибка при регистрации. Код статуса: " + response.StatusCode);
				Console.WriteLine("Текст ответа: " + await response.Content.ReadAsStringAsync());
			}
		}

		private static async Task LoginAsync()
		{
			Console.Write("Введите имя пользователя (username): ");
			string username = Console.ReadLine() ?? "";

			Console.Write("Введите пароль (password): ");
			string password = Console.ReadLine() ?? "";

			// URL: POST /login?username=...&password=...
			string url = $"/login?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}";

			var response = await _client.PostAsync(url, null);
			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

				if (dict != null && dict.TryGetValue("token", out var newToken))
				{
					_token = newToken;
					Console.WriteLine("Логин успешен! Ваш токен: " + _token);
				}
				else
				{
					Console.WriteLine("Логин успешен, но нет поля 'token' в ответе сервера.");
				}
			}
			else
			{
				Console.WriteLine("Ошибка при логине. Код статуса: " + response.StatusCode);
				Console.WriteLine("Текст ответа: " + await response.Content.ReadAsStringAsync());
			}
		}

		private static void Logout()
		{
			_token = string.Empty;
			Console.WriteLine("Вы вышли из учётной записи (токен очищен).");
		}

		// ========================================================
		//     CRUD ДЛЯ BLOG POST
		// ========================================================

		// (1) Создать пост (POST /posts?title=...&content=...)
		private static async Task CreatePostAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Введите заголовок (title): ");
			string title = Console.ReadLine() ?? "";

			Console.Write("Введите содержимое (content): ");
			string content = Console.ReadLine() ?? "";

			// Устанавливаем заголовок "Authorization: Bearer <token>"
			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			// Запрос: POST /posts?title=...&content=...
			string url = $"/posts?title={Uri.EscapeDataString(title)}&content={Uri.EscapeDataString(content)}";

			var response = await _client.PostAsync(url, null);
			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Пост успешно создан: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при создании поста. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// (2) Удалить пост (DELETE /posts/{id})
		private static async Task DeletePostAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Введите ID поста, который хотите удалить: ");
			string postIdStr = Console.ReadLine() ?? "0";
			if (!int.TryParse(postIdStr, out int postId))
			{
				Console.WriteLine("Неверный ID.");
				return;
			}

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			string url = $"/posts/{postId}";
			var response = await _client.DeleteAsync(url);

			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Пост удалён: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при удалении поста. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// (3) Обновить (PATCH /posts/{id}?title=...&content=...)
		private static async Task UpdatePostAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Введите ID поста, который хотите обновить: ");
			string postIdStr = Console.ReadLine() ?? "0";
			if (!int.TryParse(postIdStr, out int postId))
			{
				Console.WriteLine("Неверный ID.");
				return;
			}

			Console.Write("Новый заголовок (оставьте пустым, чтобы не изменять): ");
			string newTitle = Console.ReadLine() ?? "";

			Console.Write("Новое содержимое (оставьте пустым, чтобы не изменять): ");
			string newContent = Console.ReadLine() ?? "";

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			// PATCH /posts/{id}?title=...&content=...
			string url = $"/posts/{postId}?title={Uri.EscapeDataString(newTitle)}&content={Uri.EscapeDataString(newContent)}";

			var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
			{
				Content = null
			};

			var response = await _client.SendAsync(request);
			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Пост обновлён: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при обновлении поста. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// (4) Получить все посты (GET /posts)
		private static async Task GetAllPostsAsync()
		{
			if (!CheckToken()) return;

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			string url = "/posts";
			var response = await _client.GetAsync(url);

			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Список всех постов: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при получении списка постов. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// (5) Получить конкретный пост (GET /posts/{id})
		private static async Task GetPostByIdAsync()
		{
			if (!CheckToken()) return;

			Console.Write("Введите ID поста: ");
			string postIdStr = Console.ReadLine() ?? "0";
			if (!int.TryParse(postIdStr, out int postId))
			{
				Console.WriteLine("Неверный ID.");
				return;
			}

			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

			string url = $"/posts/{postId}";
			var response = await _client.GetAsync(url);

			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				Console.WriteLine("Данные поста: " + json);
			}
			else
			{
				Console.WriteLine("Ошибка при получении поста. Код: " + response.StatusCode);
				Console.WriteLine("Ответ: " + await response.Content.ReadAsStringAsync());
			}
		}

		// ========================================================
		//     ВСПОМОГАТЕЛЬНЫЙ МЕТОД
		// ========================================================
		private static bool CheckToken()
		{
			if (string.IsNullOrEmpty(_token))
			{
				Console.WriteLine("Ошибка: вы не авторизованы (нет токена).");
				return false;
			}
			return true;
		}
	}
}
