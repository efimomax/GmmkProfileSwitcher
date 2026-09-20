## GMMK 1 Profile Switcher

[🇷🇺 Русский](#русский) | [🇺🇸 English](#english)

---

<a name="english"></a>
### 🇺🇸 English

**GMMK 1 Profile Switcher** is a lightweight Windows background utility designed specifically for the original **Glorious Modular Mechanical Keyboard (GMMK 1)**. 
It automatically switches the active keyboard lighting profile (1, 2, or 3) depending on the currently active keyboard layout (input language) in Windows.

#### ✨ Features
* **Automatic Profile Switching:** Seamlessly changes your keyboard's profile when you switch languages (e.g., ALT+SHIFT) or switch to a window with a different input language.
* **System Tray Application:** Runs quietly in the background. Does not clutter your taskbar or spawn console windows.
* **Custom Layout Mapping:** Map any installed Windows language layout to Profile 1, 2, or 3 via a simple graphical interface.
* **Autorun:** Can be configured to start automatically with Windows.

#### ⚙️ Requirements
* **Keyboard:** Original Glorious Modular Mechanical Keyboard (GMMK 1)
* **OS:** Windows 10 / 11
* **Framework:** .NET Framework 4.8

#### 🚀 How to Build & Run
1. Clone the repository.
2. Open `GmmkProfileSwitcher.slnx` in **Visual Studio 2022**.
3. Set `GmmkProfileSwitcher` as the Startup Project.
4. Build and Run.

#### 🛠️ Dependencies
* [HidSharp](https://github.com/zeromq/hidsharp) (Included via NuGet) - Used for low-level USB HID communication with the keyboard.

---

<a name="русский"></a>
### 🇷🇺 Русский

**GMMK 1 Profile Switcher** — это легкая фоновая утилита для Windows, созданная специально для оригинальной механической клавиатуры **Glorious Modular Mechanical Keyboard (GMMK 1)**. 
Она автоматически переключает активный профиль подсветки клавиатуры (1, 2 или 3) в зависимости от текущей раскладки (языка ввода) в Windows.

#### ✨ Возможности
* **Автоматическое переключение:** Моментально меняет профиль на клавиатуре при смене языка (например, по ALT+SHIFT) или при переходе в окно с другой активной раскладкой.
* **Жизнь в трее:** Работает незаметно в фоновом режиме (в системном трее возле часов). Никаких висящих консольных окон.
* **Настройка привязки:** Позволяет привязать любой установленный в Windows язык к Профилю 1, 2 или 3 через удобный графический интерфейс.
* **Автозагрузка:** Умеет автоматически запускаться при старте Windows.

#### ⚙️ Системные требования
* **Клавиатура:** Оригинальная Glorious Modular Mechanical Keyboard (GMMK 1)
* **ОС:** Windows 10 / 11
* **Фреймворк:** .NET Framework 4.8

#### 🚀 Сборка и запуск
1. Склонируйте репозиторий.
2. Откройте `GmmkProfileSwitcher.slnx` в **Visual Studio 2022**.
3. Убедитесь, что проект `GmmkProfileSwitcher` назначен стартовым (Startup Project).
4. Скомпилируйте и запустите.

#### 🛠️ Зависимости
* [HidSharp](https://github.com/zeromq/hidsharp) (устанавливается через NuGet) - используется для общения с клавиатурой по USB HID.
