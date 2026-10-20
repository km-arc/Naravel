

# 🗄️ Database Query Builder

A **Laravel-inspired query builder for .NET** with fluent, chainable APIs, named connections, and automatic parameter binding across SQLite, MySQL, PostgreSQL, and SQL Server.

> **Philosophy:** Naravel.Database = Laravel DX + .NET Type Safety
>
> Write queries like Laravel. Run them like .NET.

---

## 📑 Table of Contents

- [Quick Start](#quick-start)
- [Running Queries](#running-queries)
- [Query Builder Fundamentals](#query-builder-fundamentals)
- [Advanced Filtering](#advanced-filtering)
- [Joins & Relationships](#joins--relationships)
- [Grouping & Aggregates](#grouping--aggregates)
- [Pagination](#pagination)
- [Transactions](#transactions)
- [Raw Queries](#raw-queries)
- [Observability](#observability)
- [Troubleshooting](#troubleshooting)
- [API Reference](#api-reference)

---

## 🚀 Quick Start

### Step 1: Register Services

```csharp
var builder = WebApplicationBuilder.CreateBuilder(args);

builder.Services.AddNaravelDatabase(builder.Configuration);
builder.Services.AddNaravelMySqlDatabase(builder.Configuration);
builder.Services.AddNaravelPostgresDatabase(builder.Configuration);

var app = builder.Build();
```

Step 2: Configure appsettings.json

``` json
{
  "Database": {
    "Default": "main",
    "Connections": {
      "main": {
        "Driver": "mysql",
        "Host": "localhost",
        "Port": 3306,
        "Database": "naravel\_db",
        "Username": "root",
        "Password": "secret"
      },
      "analytics": {
        "Driver": "pgsql",
        "Host": "analytics.server",
        "Database": "analytics",
        "Username": "analyst",
        "Password": "secure\_pass"
      }
    }
  }
}
```

Step 3: Inject & Use
```csharp


public class UserService(IDatabase db)
{
    public async Task<IReadOnlyList<User>> GetActiveUsersAsync(CancellationToken ct)
    {
        return await db
            .Table("users")
            .Where("active", true)
            .OrderByDesc("created\_at")
            .GetAsync<User>(ct);
    }
}
```
Use Named Connections
```csharp


public async Task<long> GetRevenueAsync(CancellationToken ct)
{
    return await db
        .Connection("analytics")  // ← Use specific connection
        .Table("transactions")
        .SumAsync<long>("amount", ct);
}
```
🔍 Running Queries
Reading Data
Get All Rows
```csharp


// As dictionaries
var rows = await db.Table("users").GetAsync(ct);

// As strongly-typed objects
var users = await db.Table("users").GetAsync<User>(ct);
```
Get First Row
```csharp


var user = await db
    .Table("users")
    .Where("email", "john@example.com")
    .FirstAsync<User>(ct);

if (user != null)
{
    Console.WriteLine(\$"Hello {user.Name}");
}
```
Get Exactly One Row
```csharp


var user = await db
    .Table("users")
    .Where("id", userId)
    .SoleAsync<User>(ct);  // Throws if 0 or 2+ rows found
```
Get By Primary Key
```csharp


var user = await db
    .Table("users")
    .FindAsync<User>(id: 42, ct);

// Custom key column
var post = await db
    .Table("posts")
    .FindAsync<Post>(id: "slug-name", keyColumn: "slug", ct);
```
Get Single Value
```csharp
var name = await db
    .Table("users")
    .Where("id", 1)
    .ValueAsync<string>("name", ct);  // → "John Doe"

var email = await db
    .Table("users")
    .Where("id", 999)
    .ValueAsync<string?>("email", ct);  // → null if not found
```

Get Column Values (Pluck)

```csharp


var emails = await db
    .Table("users")
    .Where("active", true)
    .PluckAsync<string>("email", ct);
// → ["john@example.com", "jane@example.com", ...]
```
Writing Data
Insert
```csharp


// Single row
await db.Table("users").InsertAsync(new
{
    name = "John Doe",
    email = "john@example.com",
    created\_at = DateTime.UtcNow
}, ct);

// Bulk insert
await db.Table("users").InsertAsync([
    new { name = "John", email = "john@example.com" },
    new { name = "Jane", email = "jane@example.com" }
], ct);

// Get inserted ID
var userId = await db.Table("users").InsertGetIdAsync(
    new { name = "John Doe", email = "john@example.com" },
    ct
);
```
Update
```csharp


var affected = await db
    .Table("users")
    .Where("status", "inactive")
    .UpdateAsync(new { active = false }, ct);

Console.WriteLine(\$"Updated {affected} users");
```
Delete
```csharp


var deleted = await db
    .Table("users")
    .Where("deleted\_at", "!=", null)
    .DeleteAsync(ct);
```

🏗️ Query Builder Fundamentals
SELECT Columns
```csharp


// Specific columns
db.Table("users").Select("id", "name", "email")

// Add more columns
.AddSelect("created\_at", "updated\_at")

// All columns
db.Table("users").Select("\*")

// Raw expressions
db.Table("users")
    .SelectRaw("CONCAT(first\_name, ' ', last\_name) as full\_name")
    .SelectRaw("COUNT(\*) OVER() as total\_count")

// Distinct
db.Table("users").Distinct().Select("country")
```
WHERE Conditions
Basic

```csharp


// Equality
.Where("status", "active")

// Comparison
.Where("age", ">=", 18)

// Null checks
.WhereNull("deleted\_at")
.WhereNotNull("verified\_at")

// Negation
.WhereNot("status", "banned")
```
Multiple Conditions
```csharp

// AND (chained Where)
db.Table("users")
    .Where("active", true)
    .Where("deleted\_at", null)
    .Where("role", "admin")

// OR with grouping
db.Table("users")
    .Where(q => q
        .Where("status", "vip")
        .OrWhere("status", "premium")
    )
```
IN Clause
```csharp


.WhereIn("status", ["active", "pending"])

.WhereNotIn("country", ["CN", "RU"])

// Subquery
.WhereIn("id", q => q
    .Table("admins")
    .Select("user\_id")
)

```
BETWEEN
```csharp


.WhereBetween("age", 18, 65)

.WhereNotBetween("price", 100, 500)
```
Date Operations
```csharp


.WhereDate("created\_at", "=", DateOnly.Parse("2024-01-15"))

.WhereYear("created\_at", 2024)

.WhereMonth("created\_at", ">=", 6)

.WhereDay("birthday", 25)

.WhereTime("check\_in", "<", TimeOnly.Parse("09:00"))
```
Pattern Matching
```csharp


.WhereLike("email", "%@example.com")

.WhereLike("username", "john%", caseSensitive: true)

.WhereNotLike("domain", "%.test")
```
Column Comparison
```csharp


.WhereColumn("email", "=", "confirmation\_email")

.WhereColumn("updated\_at", ">", "created\_at")
```
Conditional Builders
```csharp


.When(isAdmin, q => q
    .Select("admin\_field")
    .Where("role", "admin")
)

.Unless(isPublic, q => q
    .Where("draft", false)
)
```
🔗 Advanced Filtering
JSON Operations
JSON Contains
```csharp


// Check if JSON array contains value
.WhereJsonContains("tags", "php")

.WhereJsonDoesntContain("blocked\_countries", "US")

// With path
.WhereJsonContains("metadata->settings", "enabled")
```
JSON Key Exists
```csharp


.WhereJsonContainsKey("metadata", "github\_id")

.WhereJsonDoesntContainKey("config->deprecated")
```
JSON Array Length
```csharp


.WhereJsonLength("tags", ">", 0)

.WhereJsonLength("comments", "=", 3)
```
Full-Text Search
```csharp


// Natural language (MySQL, PostgreSQL)
.WhereFullText(["title", "description"], "running shoes")

// Boolean mode (MySQL, PostgreSQL)
.WhereFullText("content", "+laravel -wordpress", FullTextMode.Boolean)
```

Subqueries
Exists
```csharp


// WHERE EXISTS
.WhereExists(q => q
    .Table("posts")
    .Where("posts.user\_id", "users.id")
)

// WHERE NOT EXISTS
.WhereNotExists(q => q
    .Table("comments")
    .Where("comments.post\_id", "posts.id")
)
```
Comparison
```csharp


.Where("status", "=", q => q
    .Table("users\_status")
    .Select("status")
    .Where("id", userId)
)
```
🔀 Joins & Relationships
INNER JOIN
```csharp


db.Table("users")
    .Join("posts", "users.id", "=", "posts.user\_id")
    .Select("users.id", "users.name", "posts.title")
    .GetAsync<dynamic>(ct)
```
LEFT JOIN
```csharp

db.Table("users")
    .LeftJoin("posts", "users.id", "=", "posts.user\_id")
    .WhereNull("posts.id")  // Users with no posts
    .GetAsync<User>(ct)
```
RIGHT JOIN
```csharp


db.Table("posts")
    .RightJoin("users", "users.id", "=", "posts.user\_id")
    .GetAsync(ct)
```
CROSS JOIN
```csharp
db.Table("colors")
    .CrossJoin("sizes")
    .GetAsync(ct)
```
Advanced Joins
Join with Closure
```csharp
db.Table("users")
    .Join("posts", (join) => join
        .On("users.id", "=", "posts.user\_id")
        .Where("posts.status", "published")
    )
```
Multiple Joins
```csharp


db.Table("users")
    .Join("posts", "users.id", "=", "posts.user\_id")
    .Join("comments", "posts.id", "=", "comments.post\_id")
    .GetAsync<dynamic>(ct)
```
📊 Grouping & Aggregates
GROUP BY
```csharp


var results = await db
    .Table("users")
    .Select("country", "role")
    .SelectRaw("COUNT(\*) as total")
    .GroupBy("country", "role")
    .GetAsync(ct);
```
HAVING
```csharp


var results = await db
    .Table("posts")
    .Select("user\_id")
    .SelectRaw("COUNT(\*) as post\_count")
    .GroupBy("user\_id")
    .Having("post\_count", ">", 5)
    .GetAsync(ct);

```
Aggregates
COUNT
```csharp


var count = await db
    .Table("users")
    .Where("active", true)
    .CountAsync(ct);  // → long

// Count specific column
var nonNullEmails = await db
    .Table("users")
    .CountAsync("email", ct);  // Excludes nulls
```
SUM
```csharp


var totalRevenue = await db
    .Table("transactions")
    .SumAsync<decimal>("amount", ct);

// With condition
var monthlyRevenue = await db
    .Table("transactions")
    .WhereMonth("created\_at", DateTime.Now.Month)
    .SumAsync<decimal>("amount", ct);
```
AVG
```csharp


var avgAge = await db
    .Table("users")
    .AvgAsync<double>("age", ct);
```
MIN / MAX
```csharp


var minPrice = await db
    .Table("products")
    .MinAsync<decimal>("price", ct);

var maxScore = await db
    .Table("games")
    .MaxAsync<int>("score", ct);
```
📄 Pagination
Using offset() & limit()
```csharp


var page = 2;
var perPage = 15;

var users = await db
    .Table("users")
    .Offset((page - 1) * perPage)
    .Limit(perPage)
    .GetAsync<User>(ct);
```
Using paginate() Helper
```csharp


// Automatic pagination
var (items, total, lastPage) = await db
    .Table("users")
    .Where("active", true)
    .PaginateAsync<User>(
        page: 2,
        perPage: 15,
        ct
    );

Console.WriteLine(\$"Page 2 of {lastPage}");
```
Cursor Pagination
```csharp


// For large datasets (more efficient)
var (items, nextCursor) = await db
    .Table("posts")
    .OrderBy("id")
    .CursorPaginateAsync<Post>(
        cursor: "eyJpZCI6MzB9",  // Base64 encoded: {"id":30}
        limit: 20,
        ct
    );
```
💳 Transactions
Basic Transaction
```csharp

try
{
    await db.BeginTransactionAsync(ct);
    
    await db.Table("users").InsertAsync(new { name = "John" }, ct);
    await db.Table("logs").InsertAsync(new { action = "user\_created" }, ct);
    
    await db.CommitAsync(ct);
    Console.WriteLine("✅ Changes committed");
}
catch (Exception ex)
{
    await db.RollbackAsync(ct);
    Console.WriteLine(\$"❌ Rolled back: {ex.Message}");
}
```
Transaction Helper
```csharp


await db.TransactionAsync(async (scope) =>
{
    await db.Table("users").InsertAsync(new { name = "John" }, ct);
    await db.Table("logs").InsertAsync(new { action = "user\_created" }, ct);
    // Auto-commits on success, auto-rolls back on exception
}, ct);
```
Nested Transactions (Savepoints)
```csharp


await db.TransactionAsync(async (tx1) =>
{
    await db.Table("users").InsertAsync(new { name = "John" }, ct);
    
    await db.SavepointAsync("sp1", async (sp1) =>
    {
        await db.Table("posts").InsertAsync(new { title = "Hello" }, ct);
        // If this fails, only posts insert is rolled back
    }, ct);
}, ct);

```
🔧 Raw Queries
Raw SELECT
```csharp

var rows = await db.SelectAsync(
    "SELECT \* FROM users WHERE votes > ? AND status = ?",
    [100, "active"],
    ct
);

// As typed objects
var users = await db.SelectAsync<User>(
    "SELECT \* FROM users WHERE created\_at > ?",
    [DateTime.Now.AddMonths(-1)],
    ct
);
```
Raw Scalar
```csharp


var count = await db.ScalarAsync<long>(
    "SELECT COUNT(\*) FROM users WHERE deleted\_at IS NULL",
    ct
);

var avgScore = await db.ScalarAsync<double>(

```
