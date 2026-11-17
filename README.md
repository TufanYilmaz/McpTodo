# McpTodo — Model Context Protocol (MCP) Görev ve Proje Yönetim Sunucusu

Modern **.NET 8** ve **Entity Framework Core** mimarisi üzerine inşa edilmiş, **PostgreSQL (Supabase)** destekli, kurumsal düzeyde veri izolasyonu ve **OAuth 2.1 (Descope)** kimlik doğrulamasına sahip **Model Context Protocol (MCP)** sunucusudur.

McpTodo; hem **Claude Desktop** ve **Cursor** gibi yerel yapay zeka istemcileriyle (**Stdio** modu), hem de **Claude.ai Web Custom Connector** ve bulut ortamlarıyla (**Streamable HTTP / SSE** modu) çift yönlü (Dual-mode) ve kesintisiz çalışacak şekilde tasarlanmıştır.

---

## 🚀 Projede Kullanılan Teknolojiler

| Kategori | Teknoloji / Kütüphane | Açıklama & Kullanım Amacı |
| :--- | :--- | :--- |
| **Çalışma Zamanı & Çatı** | **.NET 8 (C# 12)** / ASP.NET Core | Yüksek performanslı, asenkron ve modern backend altyapısı. |
| **MCP Protokolü** | **ModelContextProtocol.AspNetCore & Server (v2.4.0)** | Resmi MCP SDK. Stdio (JSON-RPC) ve Streamable HTTP/SSE taşıma katmanları. |
| **Veritabanı & ORM** | **PostgreSQL (Supabase)** & **EF Core 8 (Npgsql)** | PostgreSQL veritabanı, Supabase IPv4 Pooler entegrasyonu ve Connection Resiliency (otomatik yeniden deneme). |
| **Veri İzolasyonu** | **EF Core Global Query Filters** | Çok kiracılı (Multi-tenant) kullanıcı veri izolasyonu (`UserId`) ve kaskad Soft-Delete (`IsDeleted = false`). |
| **Kimlik Doğrulama** | **Descope & Google OAuth** | RS256 JWKS asenkron token doğrulama, kullanıcı profil yönetimi ve Descope Agentic Hub entegrasyonu. |
| **OAuth 2.1 Standartları** | **RFC 9728, RFC 8414, RFC 7591** | Claude.ai ve uzaktan yapay zeka istemcileri için dinamik istemci kaydı (DCR) ve kaynak sunucu keşfi. |
| **Birim Test & Kalite** | **xUnit, Moq, Coverlet** | 91 adet birim test, %100 çekirdek sınıf kod kapsama oranı (Code Coverage). |
| **Konteynerizasyon** | **Docker (Alpine Linux non-root)** | Güvenli `USER app` mimarisiyle hafif, üretime hazır mikroservis imajı. |
| **CI / CD Pipeline** | **GitHub Actions & SonarCloud** | Otomatik derleme, SonarCloud statik kod analizi, GHCR imaj dağıtımı ve multi-OS release paketleme. |

---

## 🛠️ Sunulan MCP Araçları (Tools)

Sunucu, yapay zeka asistanlarının doğrudan çağırabileceği 7 temel araç sunar:

### Proje Yönetimi
- `project_list`: Kullanıcının aktif tüm projelerini listeler.
- `project_create`: Yeni bir proje oluşturur (`name` parametresi zorunludur).
- `project_delete`: Belirtilen projeyi ve bağlı tüm görevleri kaskad biçimde soft-delete ile siler (`id` parametresi zorunludur).

### Görev (Todo) Yönetimi
- `todo_list`: Görevleri listeler. İsteğe bağlı `projectId` ve `isCompleted` parametreleriyle filtrelenebilir. Sonuçları Markdown formatında (projelere göre gruplandırılmış) döndürür.
- `todo_add`: Yeni bir görev ekler (`title` zorunlu; `projectId` ve `dueDate` opsiyoneldir).
- `todo_complete`: Belirtilen görevi tamamlandı (`isCompleted = true`) olarak işaretler.
- `todo_delete`: Görevi soft-delete ile siler.

---

## 🔌 İstemci Bağlantı Rehberi (Connector Kurulumu)

McpTodo sunucusuna 3 farklı yöntemle bağlanabilirsiniz:

```
                            ┌────────────────────────────────────────┐
                            │           McpTodo Server               │
                            │       (Dual-Mode Architecture)         │
                            └────────────────────┬───────────────────┘
                                                 │
                   ┌─────────────────────────────┴─────────────────────────────┐
                   │                                                           │
        [ HTTP / SSE Mode ]                                            [ Stdio Mode ]
        RFC 9728 / 8414 / 7591                                         JSON-RPC over Pipes
                   │                                                           │
       ┌───────────┴───────────┐                                   ┌───────────┴───────────┐
       ▼                       ▼                                   ▼                       ▼
   Claude.ai                Cursor                              Claude                  Cursor
(Custom Connector       (SSE Remote)                            Desktop                 (Local CLI)
  + Descope OAuth)
```

---

### 1. Claude.ai Web Custom Connector (Bulut / OAuth 2.1 ile Otomatik Giriş)

Claude.ai web arayüzünde uzak bir MCP sunucusu olarak eklemek için en ideal yöntemdir. Sunucumuz **RFC 9728 (Protected Resource Metadata)**, **RFC 8414 (Authorization Server Metadata)** ve **RFC 7591 (Dynamic Client Registration)** standartlarını desteklediğinden Claude.ai ile tam uyumlu çalışır.

1. **Claude.ai** üzerinde sol alt profilinizden **Settings** -> **Connectors** (veya sohbet penceresindeki **Add Connector**) bölümüne gidin.
2. **Add Custom Connector** seçeneğine tıklayın.
3. Bilgileri aşağıdaki gibi doldurun:
   - **Connector Name:** `todo`
   - **MCP Server URL:** `https://mcptodo.onrender.com/mcp` *(veya kendi Render / sunucu adresiniz)*
   - **Authentication:** `OAuth` seçeneğini işaretleyin.
4. **Connect / Add** butonuna basın.
5. **Yetkilendirme Akışı:**
   - Claude.ai, sunucuya bir el sıkışma isteği atar (`401 Unauthorized` + `WWW-Authenticate`).
   - Claude, sunucunun `/.well-known/oauth-protected-resource/mcp` ve `/.well-known/oauth-authorization-server` uç noktalarından Descope Agentic Hub bilgilerini otomatik okur.
   - Claude, `/register` uç noktasına dinamik istemci kaydı isteği gönderir ve sunucu bu isteği Descope'a ileterek Claude için anında bir `client_id` üretir.
   - Tarayıcınızda açılan Descope / Google giriş ekranından oturum açıp izin verin.
6. Bağlantı tamamlandığında Claude.ai sohbetinde "todo araçlarını kullanarak projelerimi listele" diyerek kullanmaya başlayabilirsiniz!

---

### 2. Claude Desktop Entegrasyonu (Yerel Masaüstü - Stdio Modu)

Claude Desktop uygulamasını yerel makinenizde çalıştırmak için:

1. [GitHub Releases](https://github.com/OWNER/REPO/releases) sayfasından işletim sisteminize uygun derlenmiş ikili dosyayı indirin (Örn: Windows için `mcp-todo.exe`).
2. Dosyayı kalıcı bir klasöre koyun (Örn: `C:\Tools\mcp-todo\mcp-todo.exe`).
3. Claude Desktop yapılandırma dosyasını açın:
   - **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`
   - **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`
4. `mcpServers` bölümüne aşağıdaki yapılandırmayı ekleyin:

```json
{
  "mcpServers": {
    "todo": {
      "command": "C:\\Tools\\mcp-todo\\mcp-todo.exe",
      "args": [],
      "env": {
        "ConnectionStrings__Supabase": "Host=aws-0-eu-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.xxxxx;Password=BURAYA_SIFRENIZ;SSL Mode=Require;Trust Server Certificate=true",
        "DESCOPE_PROJECT_ID": "BURAYA_DESCOPE_PROJECT_ID",
        "DESCOPE_USER_TOKEN": "BURAYA_DESCOPE_JWT_TOKEN"
      }
    }
  }
}
```

> **Not:** Claude Desktop uygulamasını yeniden başlattığınızda sohbet kutusunun sağ altındaki çekiç simgesinde `todo` araçlarının aktifleştiğini göreceksiniz.

---

### 3. Cursor IDE Entegrasyonu

Cursor IDE üzerinden McpTodo'ya iki farklı şekilde bağlanabilirsiniz:

#### Yöntem A: Uzak SSE Bağlantısı (Önerilen)
1. Cursor içinde `Ctrl + Shift + J` (veya `Cmd + Shift + J`) ile **Cursor Settings**'i açın.
2. **Features** -> **MCP Servers** -> **+ Add New MCP Server** butonuna tıklayın.
3. Alanları doldurun:
   - **Name:** `mcp-todo`
   - **Type:** `sse`
   - **URL:** `https://mcptodo.onrender.com/mcp`
4. Kaydedin.

#### Yöntem B: Yerel Stdio (Command) Bağlantısı
- **Type:** `command`
- **Command:** `C:\Tools\mcp-todo\mcp-todo.exe` (Linux/macOS için: `/path/to/mcp-todo`)
- **Environment Variables:**
  - `ConnectionStrings__Supabase`: Supabase bağlantı dizeniz
  - `DESCOPE_PROJECT_ID`: Descope Proje ID'niz
  - `DESCOPE_USER_TOKEN`: Descope JWT Token'ınız

---

## ⚙️ Ortam Değişkenleri (Environment Variables)

Sunucuyu Docker, Render veya yerel ortamda yapılandırmak için aşağıdaki değişkenler kullanılır:

| Değişken | Zorunlu mu? | Açıklama |
| :--- | :---: | :--- |
| `ConnectionStrings__Supabase` | **Evet** | PostgreSQL / Supabase bağlantı dizesi (IPv4 Pooler önerilir). |
| `DESCOPE_PROJECT_ID` | **Evet** | Descope Proje Kimliği (Örn: `Peuc1...`). |
| `DESCOPE_APP_ID` | Web modunda | Descope Agentic Application Kimliği (Örn: `RS3ME...`). |
| `SERVER_URL` | Web modunda | Sunucunun genel erişim adresi (Örn: `https://mcptodo.onrender.com`). |
| `PORT` | Hayır | HTTP sunucusunun dinleyeceği port (Varsayılan: `8080`, Render otomatik sağlar). |
| `DESCOPE_USER_TOKEN` | Stdio modunda | Yerel CLI kullanımında kullanıcı oturumunu belirleyen JWT token. |

---

## 💻 Yerel Geliştirme ve Test

Projeyi yerel geliştirme ortamınızda derlemek ve testleri çalıştırmak için:

### 1. Depoyu Klonlayın
```bash
git clone https://github.com/OWNER/REPO.git
cd mcp-todo
```

### 2. Testleri Çalıştırın
Tüm birim testleri ve kod kapsama raporunu çalıştırmak için:
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### 3. Sunucuyu Başlatın
- **Web / HTTP Modunda Başlatma:**
  ```bash
  dotnet run --project mcp-todo/mcp-todo --http
  ```
- **Araç Doğrulama Kontrolü:**
  ```bash
  dotnet run --project mcp-todo/mcp-todo --verify-tools
  ```

### 4. Docker ile Çalıştırma
```bash
docker build -t mcp-todo:latest -f Dockerfile .
docker run -p 8080:8080 \
  -e ConnectionStrings__Supabase="<CONNECTION_STRING>" \
  -e DESCOPE_PROJECT_ID="<PROJECT_ID>" \
  -e DESCOPE_APP_ID="<APP_ID>" \
  -e SERVER_URL="http://localhost:8080" \
  mcp-todo:latest
```

---

## 🔒 Güvenlik & Mimari İlkeleri

- **Sıfır Güven (Zero Trust) Multi-Tenancy:** Hiçbir kullanıcı başka bir kullanıcının görevlerini veya projelerini görüntüleyemez, güncelleyemez ya da silemez. Tüm sorgular EF Core Global Query Filter ile zorunlu kılınmıştır.
- **Kaskad Soft-Delete:** Bir proje silindiğinde veritabanından kalıcı olarak silinmez; proje ve ona ait tüm görevler `IsDeleted = true` yapılarak arşivlenir.
- **Güvenli Konteyner İmajı:** Docker imajı root yetkileriyle değil, düşük yetkili `app` kullanıcısı ile çalışır.
- **RFC Uyumlu OAuth:** Token doğrulama işlemi Descope'un resmi JWKS anahtarları ile RS256 algoritmasıyla doğrulanır. Dynamic Client Registration (DCR) uç noktası ile istemci kayıtları güvenli proxy üzerinden yönetilir.

---

## 📄 Lisans

Bu proje MIT lisansı ile lisanslanmıştır. Detaylar için `LICENSE` dosyasına bakabilirsiniz.
