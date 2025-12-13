# Quip

**A Dynamic DNS tool that updates changes to DNS services**

A Blazor Server application for managing dynamic DNS updates. Keep your domains pointed at the right IP addresses, automatically.

## Tech Stack

- .NET 10.0 / Blazor Server (InteractiveServer mode)
- MudBlazor UI Framework
- SQLite with Entity Framework Core
- Basic Username/Password Authentication
- Serilog for Structured Logging

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- Docker & Docker Compose (for containerized development)

### Local Development

1. **Copy environment file:**
   ```bash
   cp .env.example .env
   ```

2. **Run the application:**
   ```bash
   cd Quip
   dotnet run
   ```

3. **Access the app:** https://localhost:5001

### Docker Development

1. **Copy environment file:**
   ```bash
   cp .env.example .env
   ```

2. **Start everything:**
   ```bash
   docker-compose up
   ```

3. **Access the app:** http://localhost:5000

## Project Structure

```
Quip/
├── Quip/
│   ├── Application/           # UI Layer
│   │   ├── Areas/            # Feature areas (Home, etc.)
│   │   ├── Components/       # Shared components
│   │   └── Layout/           # MainLayout, AppVM
│   ├── Data/                 # DbContext
│   ├── DataAdapters/         # Data access (Readers/Writers)
│   ├── Models/               # Domain entities
│   ├── Services/             # Business logic (Auth, etc.)
│   └── wwwroot/              # Static assets
├── docker-compose.yml
├── .env.example
└── Quip.sln
```

## Architecture

This project follows **MVVM** architecture:

- **Views** (Razor components) - Zero logic, bind to ViewModels
- **ViewModels** - UI state translation only
- **Services** - All business logic
- **DataAdapters** - Focused data access (Reader/Writer pattern)

## Configuration

### Environment Variables

The application uses a `.env` file for configuration. Copy `.env.example` to `.env` and configure as needed:

```bash
cp .env.example .env
```

**Email Configuration (Optional):**

To enable email verification for new user setup, configure SMTP settings in `.env`:

```bash
# Uncomment and configure these to enable email sending:
Email__SmtpHost=smtp.gmail.com
Email__SmtpPort=587
Email__SmtpUsername=your-email@gmail.com
Email__SmtpPassword=your-app-password
Email__FromEmail=your-email@gmail.com
Email__FromName=Quip
```

**Gmail Setup:**
1. Enable 2-factor authentication on your Google account
2. Generate an App Password: https://myaccount.google.com/apppasswords
3. Use the App Password (not your regular password) in `Email__SmtpPassword`

**Note:** If SMTP is not configured, verification codes will be logged to the console during development.

### VS Code Debugging

The project includes VS Code launch configuration with `.env` file support. To debug:

1. Open the project in VS Code
2. Press `F5` or use the "Run and Debug" panel
3. Select ".NET Core Launch (web)"

The `.env` file will be automatically loaded when debugging.

### Layout
- Dense header (no drawers)
- 80% body width, centered
- Scrollable content

### Database
<<<<<<< Updated upstream
SQLite is used for data storage. The database file is created automatically at `quip.db`.
=======
SQLite is used for data storage. The database file is created automatically at `.sqlite/stagezero.db`.
>>>>>>> Stashed changes

## License

Private - All rights reserved

