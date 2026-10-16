# Query builder coverage ledger (PDR-022)

Source: the Laravel "Database: Query Builder" page. Every method group is assigned to exactly one stage so "all methods" is checkable.
This file lists **ownership only**; the status of a stage lives in [ROADMAP.md](../ROADMAP.md). C# names are PascalCase with an `Async`
suffix on anything that executes. A method that is deliberately not ported says so and why.

| Laravel group | Methods | Stage |
|---|---|---|
| Raw statements (`DB::`) | `select`, `statement`, `affectingStatement`, `unprepared`, `scalar` | R19 |
| Query log / listen | `DB::listen`, `enableQueryLog`, `getQueryLog` | R19 |
| Running queries | `table`, `get`, `first`, `sole`, `value`, `find`, `pluck` | R19 |
| Selects | `select`, `addSelect`, `distinct` | R19 |
| Basic where | `where` (column/operator/value, array form, closure group), `orWhere`, `whereNot`, `orWhereNot` | R19 |
| Ordering, limit | `orderBy`, `orderByDesc`, `limit`/`take`, `offset`/`skip` | R19 |
| Dialects | MySQL, PostgreSQL, SQL Server grammars and drivers | R20 |
| Additional where | `whereIn`, `whereNotIn`, `whereBetween`, `whereNotBetween`, `whereBetweenColumns`, `whereNull`, `whereNotNull`, `whereColumn`, `orWhere*` of each | R21 |
| Date where | `whereDate`, `whereMonth`, `whereDay`, `whereYear`, `whereTime` | R21 |
| Multi-column where | `whereAny`, `whereAll`, `whereNone` | R21 |
| Pattern / full text | `whereLike`, `whereNotLike`, `whereFullText` | R21 |
| JSON where | `whereJsonContains`, `whereJsonDoesntContain`, `whereJsonContainsKey`, `whereJsonLength`, `->` column paths | R21 |
| Logical grouping, exists | nested closures, `whereExists`, `whereNotExists`, subquery where | R21 |
| Joins | `join`, `leftJoin`, `rightJoin`, `crossJoin`, advanced join clauses (`on`, `orOn`, `where` inside join) | R21 |
| Grouping | `groupBy`, `having`, `orHaving`, `havingBetween`, `havingNull` | R21 |
| Ordering helpers | `latest`, `oldest`, `inRandomOrder`, `reorder`, `reorderDesc` | R21 |
| Conditional clauses | `when`, `unless` | R21 |
| Aggregates | `count`, `min`, `max`, `sum`, `avg`, `exists`, `doesntExist` | R22 |
| Chunking / streaming | `chunk`, `chunkById`, `chunkMap`, `lazy`, `lazyById`, `lazyByIdDesc`, `cursor` (as `IAsyncEnumerable`), `each`, `eachById` | R22 |
| Insert | `insert`, `insertOrIgnore`, `insertGetId`, `insertUsing` | R23 |
| Update | `update`, `updateOrInsert`, JSON column update, `increment`, `decrement`, `incrementEach`, `decrementEach` | R23 |
| Upsert | `upsert` | R23 |
| Delete | `delete`, `truncate` | R23 |
| Returning | `RETURNING`/`OUTPUT` where the dialect has it | R23 |
| Unions | `union`, `unionAll` | R24 |
| Subqueries | `selectSub`, `fromSub`, `joinSub`, `leftJoinSub`, `rightJoinSub`, `joinLateral`, `leftJoinLateral`, `whereSub` forms | R24 |
| CTE | `withExpression`, recursive CTE | R24 |
| Raw expressions | `DB::raw`, `selectRaw`, `whereRaw`, `orWhereRaw`, `havingRaw`, `orHavingRaw`, `orderByRaw`, `groupByRaw`, `fromRaw` | R24 |
| Locking | `sharedLock`, `lockForUpdate` | R24 |
| Debugging | `toSql`, `toRawSql`, `dump*`/`dd*` (not ported: PHP debug helpers; use `ToSql`/`ToRawSql` and the query log) | R24 |
| Transactions | `DB::transaction`, `beginTransaction`, `commit`, `rollBack`, deadlock retries, savepoints | R25 |
| EF Core interop | shared connection and transaction with `DbContext` | R25 |
| Pagination | `paginate`, `simplePaginate`, `cursorPaginate` (uses R16 types) | R26 |
| Not in scope | Eloquent models and relations, schema builder, migrations (use EF Core), seeders | - |
