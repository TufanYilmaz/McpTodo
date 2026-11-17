# McpTodo

.NET 8 ve Entity Framework Core tabanlı Todo MCP (Model Context Protocol) servisi. PostgreSQL / Supabase veritabanı entegrasyonu, soft delete ve kullanıcı bazlı global sorgu filtreleri içerir.

## Özellikler

- **.NET 8** & **Entity Framework Core**
- **PostgreSQL / Supabase** desteği (Npgsql)
- **Global Query Filters** (Multi-tenancy & Soft Delete)
- **Docker** desteği

## Başlangıç

### Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- PostgreSQL veya [Supabase](https://supabase.com/) hesabı
- Docker (isteğe bağlı)

### Yapılandırma

Supabase bağlantı dizesini .NET User Secrets veya ortam değişkeni ile belirleyin:

```bash
dotnet user-secrets set "ConnectionStrings:Supabase" "<SUPABASE_CONNECTION_STRING>"
```

veya ortam değişkeni:

```bash
set ConnectionStrings__Supabase="<SUPABASE_CONNECTION_STRING>"
```

### Çalıştırma

```bash
dotnet run --project mcp-todo
```
