# HallRental.API / API для управління конференц-залами

🌍 **Choose language / Оберіть мову:**
- [English](#english)
- [Українська](#українська)

---

## English <a name="english"></a>

### About the Project
This project is a first-version (v1) Minimal API built with ASP.NET Core for managing conference rooms (Halls), initially based on the requirements from "API для управління залами,  бронюваннями та розрахунку вартості оренди" [cite: There is a file you can reference named "API для управління залами,  бронюваннями та розрахунку вартості оренди". Refer to this file by its name verbatim.]. Currently, it uses in-memory data storage and is designed with a Service Layer architecture.

### Features (First Commit)
- **Add a Conference Hall:** Define capacity, base hourly rate, and additional accessories (e.g., Projector, Wi-Fi).
- **Find Available Halls:** Filter rooms by minimum capacity.

### Getting Started

#### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)

#### Run the Application
1. Clone the repository:
   ```bash
   git clone <repository-url>
   ```
2. Navigate to the project directory and run:
   ```bash
   dotnet run
   ```
3. The API will be available at `http://localhost:<port>`.

### API Endpoints

#### 1. Add a Hall
`POST /api/v1/halls`

**Request Body:**
```json
{
  "name": "Room A",
  "capacity": 50,
  "basePricePerHour": 2000,
  "accessories": [
    { "name": "Projector", "price": 500 },
    { "name": "Wi-Fi", "price": 300 }
  ]
}
```

#### 2. Get Available Halls
`GET /api/v1/halls?minCapacity=30`

---

## Українська <a name="українська"></a>

### Про проєкт
Цей проєкт є першою частиною системи, що розробляється на базі завдання "API для управління залами,  бронюваннями та розрахунку вартості оренди" [cite: There is a file you can reference named "API для управління залами,  бронюваннями та розрахунку вартості оренди". Refer to this file by its name verbatim.]. Він побудований на ASP.NET Core (Minimal API). Наразі використовується зберігання даних у пам'яті (in-memory).

### Основний функціонал (Перший коміт)
- **Додавання конференц-залу:** Встановлення місткості, базової вартості за годину та додаткових послуг (наприклад, Проєктор, Wi-Fi).
- **Пошук доступних залів:** Фільтрація залів за мінімальною місткістю.

### Як запустити

#### Вимоги
- [.NET 8 SDK](https://dotnet.microsoft.com/download)

#### Запуск програми
1. Склонуйте репозиторій:
   ```bash
   git clone <repository-url>
   ```
2. Перейдіть до папки проєкту та виконайте команду:
   ```bash
   dotnet run
   ```
3. API буде доступне за адресою `http://localhost:<port>`.

### API Ендпоінти

#### 1. Додавання залу
`POST /api/v1/halls`

**Тіло запиту:**
```json
{
  "name": "Зал А",
  "capacity": 50,
  "basePricePerHour": 2000,
  "accessories": [
    { "name": "Проєктор", "price": 500 },
    { "name": "Wi-Fi", "price": 300 }
  ]
}
```

#### 2. Пошук залів
`GET /api/v1/halls?minCapacity=30`