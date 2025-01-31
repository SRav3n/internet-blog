using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using BCrypt.Net;
using System.Text.RegularExpressions;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);

// 1. Регистрируем DbContext и указываем строку подключения к SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
{
	options.UseSqlite("Data Source=blog.db");
});

// Подключаем контроллеры (если захотим использовать Swagger/Minimal APIs)
builder.Services.AddControllers().AddJsonOptions(opts =>
{
	opts.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// Для документирования (Swagger)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 2. Создаём/мигрируем БД (упрощённо — EnsureCreated)
using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
	db.Database.EnsureCreated();
}

// Подключаем Swagger (в Dev-режиме)
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

// Если нужно HTTPS
app.UseHttpsRedirection();



// Извлечение Bearer-токена из заголовка Authorization
static string? ExtractBearerToken(string authHeader)
{
	if (string.IsNullOrEmpty(authHeader)) return null;
	var match = Regex.Match(authHeader, @"Bearer\s+(.*)", RegexOptions.IgnoreCase);
	if (match.Success)
		return match.Groups[1].Value.Trim();
	return null;
}

// =======================
// ЭНДПОИНТЫ (Minimal API)
// =======================

// ----------  Подсистема регистрации и авторизации  ----------

// (1) Регистрация нового пользователя
app.MapPost("/register", async (AppDbContext db, string username, string password) =>
{
	if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
	{
		return Results.BadRequest(new { error = "Username and password are required" });
	}

	// Проверка на существующего пользователя
	var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
	if (existingUser != null)
	{
		return Results.BadRequest(new { error = "User already exists" });
	}

	// Создаём хеш для пароля
	var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
	// Генерируем токен
	var token = AuthUtils.GenerateToken();

	var user = new User
	{
		Username = username,
		PasswordHash = passwordHash,
		Token = token
	};

	db.Users.Add(user);
	await db.SaveChangesAsync();

	return Results.Created("/register", new
	{
		message = "User registered successfully",
		token = user.Token
	});
});

// (2) Логин (если потребуется) — авторизация по паролю
// Возвращает токен, если пароль верный
app.MapPost("/login", async (AppDbContext db, string username, string password) =>
{
	// Ищем пользователя
	var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
	if (user == null)
	{
		return Results.Unauthorized(); // нет такого пользователя
	}

	// Проверяем пароль
	if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
	{
		return Results.Unauthorized(); // неверный пароль
	}

	// Генерируем новый токен (можно не генерировать, если хотим использовать старый)
	user.Token = AuthUtils.GenerateToken();
	await db.SaveChangesAsync();

	return Results.Ok(new
	{
		message = "Login successful",
		token = user.Token
	});
});

// ----------  Подсистема управления контентом (статьи)  ----------
// Для всех операций ниже требуется авторизация (Bearer <token>).

// (A) Вспомогательная функция, чтобы получить текущего пользователя по токену
async Task<User?> GetCurrentUser(AppDbContext db, HttpRequest req)
{
	var authHeader = req.Headers["Authorization"].ToString();
	var token = ExtractBearerToken(authHeader);
	if (string.IsNullOrEmpty(token)) return null;

	var user = await db.Users.FirstOrDefaultAsync(u => u.Token == token);
	return user;
}

// (3) Добавить запись (POST /posts)
app.MapPost("/posts", async (AppDbContext db, HttpRequest req, string title, string content) =>
{
	var user = await GetCurrentUser(db, req);
	if (user == null) return Results.Unauthorized();

	// Проверка на пустые поля
	if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
	{
		return Results.BadRequest(new { error = "Title and content are required" });
	}

	var post = new BlogPost
	{
		UserId = user.Id,
		Title = title,
		Content = content,
		CreatedAt = DateTime.UtcNow,
		UpdatedAt = DateTime.UtcNow
	};

	db.BlogPosts.Add(post);
	await db.SaveChangesAsync();

	return Results.Created("/posts", new
	{
		message = "Post created",
		postId = post.Id
	});
});

// (4) Удалить запись (DELETE /posts/{id})
app.MapDelete("/posts/{id}", async (AppDbContext db, HttpRequest req, int id) =>
{
	var user = await GetCurrentUser(db, req);
	if (user == null) return Results.Unauthorized();

	var post = await db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id);
	if (post == null) return Results.NotFound(new { error = "Post not found" });

	// Проверяем, что автор поста — текущий пользователь (или права админа, если потребуется)
	if (post.UserId != user.Id)
	{
		return Results.Forbid(); // или 403
	}

	db.BlogPosts.Remove(post);
	await db.SaveChangesAsync();

	return Results.Ok(new { message = "Post deleted" });
});

// (5) Отредактировать запись (PATCH /posts/{id})
app.MapMethods("/posts/{id}", new[] { "PATCH" }, async (AppDbContext db, HttpRequest req, int id, string? title, string? content) =>
{
	var user = await GetCurrentUser(db, req);
	if (user == null) return Results.Unauthorized();

	var post = await db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id);
	if (post == null) return Results.NotFound(new { error = "Post not found" });

	if (post.UserId != user.Id)
	{
		return Results.Forbid();
	}

	// Обновляем только те поля, которые переданы
	bool updated = false;
	if (!string.IsNullOrEmpty(title))
	{
		post.Title = title;
		updated = true;
	}
	if (!string.IsNullOrEmpty(content))
	{
		post.Content = content;
		updated = true;
	}

	if (updated)
	{
		post.UpdatedAt = DateTime.UtcNow;
		await db.SaveChangesAsync();
	}

	return Results.Ok(new { message = "Post updated" });
});

// (6) Просмотреть все записи (GET /posts)
app.MapGet("/posts", async (AppDbContext db) =>
{
	// Возвращаем список всех постов (можно добавить пагинацию)
	var posts = await db.BlogPosts
		.OrderByDescending(p => p.CreatedAt)
		.Select(p => new
		{
			p.Id,
			p.UserId,
			p.Title,
			p.Content,
			p.CreatedAt,
			p.UpdatedAt
		})
		.ToListAsync();

	return Results.Ok(posts);
});

// (7) Просмотреть конкретную запись (GET /posts/{id})
app.MapGet("/posts/{id}", async (AppDbContext db, int id) =>
{
	var post = await db.BlogPosts
		.Where(p => p.Id == id)
		.Select(p => new
		{
			p.Id,
			p.UserId,
			p.Title,
			p.Content,
			p.CreatedAt,
			p.UpdatedAt
		})
		.FirstOrDefaultAsync();

	if (post == null) return Results.NotFound(new { error = "Post not found" });
	return Results.Ok(post);
});

// Запуск приложения
app.Run();


// =======================
// МОДЕЛИ и КОНТЕКСТ БД
// =======================
public class AppDbContext : DbContext
{
	public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

	// Таблица пользователей
	public DbSet<User> Users => Set<User>();
	// Таблица статей (постов)
	public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
}

// Пользователь
public class User
{
	public int Id { get; set; }
	public string Username { get; set; } = null!;
	public string PasswordHash { get; set; } = null!;
	public string Token { get; set; } = null!;
}

// Блог-пост
public class BlogPost
{
	public int Id { get; set; }
	public int UserId { get; set; }  // Автор поста (связка с User)
	public string Title { get; set; } = null!;
	public string Content { get; set; } = null!;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// =======================
// ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
// =======================

// Генерация произвольного токена. Можно заменить на JWT или Guid
static class AuthUtils
{
	public static string GenerateToken()
	{
		return Guid.NewGuid().ToString("N");
	}
}