<img src="PasskeyPick/YubiKey.ico" height="24" alt="YubiKey 5 NFC USB-A" /> PasskeyPick
===

[![Download count](https://img.shields.io/github/downloads/f1owkang/PasskeyPick/total?logo=github)](https://github.com/f1owkang/PasskeyPick/releases) [![Build status](https://img.shields.io/github/actions/workflow/status/f1owkang/PasskeyPick/dotnet.yml?branch=master&logo=github)](https://github.com/f1owkang/PasskeyPick/actions/workflows/dotnet.yml)

*带系统托盘图标的后台程序，自动跳过「配对手机」选项，并在 Windows FIDO/WebAuthn 弹窗中自动选择「USB 安全密钥」。*
*Background program with a system tray icon that skips the phone pairing option and chooses the USB security key in Windows FIDO/WebAuthn prompts.*

<!-- MarkdownTOC autolink="true" bracket="round" autoanchor="false" levels="1,2" -->

- [问题 / Problem](#问题--problem)
- [解决方案 / Solution](#解决方案--solution)
- [系统要求 / Requirements](#系统要求--requirements)
- [安装 / Installation](#安装--installation)
- [演示 / Demo](#演示--demo)
- [构建 / Building](#构建--building)
- [相关 / Related](#相关--related)

<!-- /MarkdownTOC -->

## 问题 / Problem

**中文**：当浏览器等程序通过 WebAuthn 请求认证时，Windows 会显示安全凭据弹窗，可用 USB 安全密钥，或保存在电脑 TPM 中、由 Windows Hello PIN 或生物识别保护的通行密钥完成认证。

Windows 11 [22H2 Moment 4](https://www.bleepingcomputer.com/news/microsoft/windows-11-moment-4-update-released-here-are-the-many-new-features/)（2023 年 9 月）及更高版本（含 [23H2](https://www.bleepingcomputer.com/news/microsoft/windows-11-23h2-new-features-in-the-windows-11-2023-update/)）新增了「选择通行密钥」步骤：弹窗要求先指明使用「iPhone、iPad 或 Android 设备」还是「安全密钥」，选择 USB 安全密钥需额外一次点击或三次按键；即使关闭蓝牙、没有 Android/iOS 设备也无法跳过该弹窗，且 Windows 不会记住你的上次选择。在此之前的版本中，若 TPM 中没有所需密钥，Windows 会直接提示插入 USB 安全密钥。

<p align="center"><img src=".github/images/usb-prompt.png" alt="usb security key prompt" width="456" /></p>

<p align="center"><img src=".github/images/authenticator-prompt.png" alt="authenticator prompt" width="456" /></p>

**English**: When a program (such as a WebAuthn-capable browser) requests authentication, Windows can show a security credential prompt that lets you authenticate with a USB security key or a passkey stored in the computer's TPM and protected by Windows Hello PIN or biometrics.

Windows 11 [22H2 Moment 4](https://www.bleepingcomputer.com/news/microsoft/windows-11-moment-4-update-released-here-are-the-many-new-features/) (September 2023) and later (including [23H2](https://www.bleepingcomputer.com/news/microsoft/windows-11-23h2-new-features-in-the-windows-11-2023-update/)) added a "Choose a passkey" step: the prompt first asks whether to use an "iPhone, iPad, or Android device" or a "Security key", and picking the USB security key costs an extra click or three keystrokes. You cannot opt out of this step even without Bluetooth or any Android/iOS device, and Windows does not remember your choice. In earlier versions, if the TPM did not contain the required key, Windows would directly prompt you to insert a USB security key.

## 解决方案 / Solution

**中文**：这是一个在 Windows 用户会话中后台运行、**驻留系统托盘**的程序（托盘图标带右键菜单，可启用/禁用自动选择、切换首选验证方法、切换语言、缓存安全密钥 PIN 等，详见[系统托盘图标](#系统托盘图标--system-tray-icon)）。它等待 Windows FIDO 凭据提供程序弹窗出现，然后自动为你选择「安全密钥」选项。从用户的角度看，蓝牙界面几乎刚出现就被替换为「插入你的 USB 安全密钥」的提示。

<p align="center"><img src=".github/images/demo.gif" alt="demo" width="465" /></p>     

在内部，本程序使用 [Microsoft UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview) 来读取并操作这些对话框。

**English**: This is a background program that runs in your Windows user session with a **system tray GUI** — a tray icon with a right-click menu for enabling/disabling automatic selection, choosing the preferred authenticator, switching the language, and caching the security key PIN (see [System tray icon](#系统托盘图标--system-tray-icon)). It waits for Windows FIDO credential provider prompts to appear, then chooses the Security Key option for you automatically. From the user's perspective, the Bluetooth screen barely even appears before it's replaced with the prompt to plug in your USB security key.

Internally, this program uses [Microsoft UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview) to read and interact with the dialog boxes.

### 覆盖自动行为 / Overriding the automatic next behavior

**中文**：默认情况下，本程序不会干预本地 TPM 通配密钥弹窗（例如请求输入 Windows Hello PIN 或生物识别）。如果 FIDO 弹窗中包含除「USB 安全密钥」和「配对新的蓝牙手机」以外的其他选项——例如你已配对过手机，或你之前拒绝过 PIN 等 Windows Hello 因子而现在想从认证器选择弹窗中再次尝试 PIN——程序也不会自动提交。如果你希望在大多数情况下都优先选择 USB 安全密钥，可以在托盘菜单的「首选验证方法」中选中「USB 安全密钥」（详见[系统托盘图标](#系统托盘图标--system-tray-icon)）；仅当你想强制在**所有**情况下都选择 USB 安全密钥、即使还有其他有效选项（如 Windows Hello PIN/生物识别）时，才使用命令行参数 `--skip-all-non-security-key-options`。

如果对话框中出现已配对的手机选项而你想移除它，可以编辑注册表取消配对已有手机（当你的旧手机[变砖](https://en.wikipedia.org/wiki/Pixel_5a#Known_issues)或刚换新手机时，这会很有用）。

如果本程序在你不想让它跳过的时候跳过了认证器选择弹窗（例如你只是想偶尔用一次手机蓝牙通配密钥），你可以在弹窗出现时按住 <kbd>Shift</kbd>，临时禁止本程序自动提交一次安全密钥选择。

即使本程序没有点击「下一步」按钮（因为存在额外选项，或你正按住 <kbd>Shift</kbd>），它仍会高亮「安全密钥」选项并聚焦「下一步」按钮，因此你只需按 <kbd>Enter</kbd> 或 <kbd>Space</kbd> 即可选择安全密钥。

**English**: By default, this program does not interfere with local TPM passkey prompts (like requesting your Windows Hello PIN or biometrics). It also does not automatically submit FIDO prompts that contain additional options besides a USB security key and pairing a new Bluetooth smartphone, such as the cases when you already have a paired phone, or you previously declined a Windows Hello factor like a PIN but want to try a PIN again from the authenticator choice dialog. If you'd like to prefer the USB security key in most cases, choose "USB security key" under **Preferred authenticator** in the tray menu (see [System tray icon](#系统托盘图标--system-tray-icon)); only use the `--skip-all-non-security-key-options` command-line argument if you want to force the USB security key in **_all_** cases, even when other valid options like Windows Hello PIN/biometrics are present.

If a paired phone option appears in the dialog box and you want to remove it, you can edit the registry to unpair an existing phone (useful if your old phone [bricked itself](https://en.wikipedia.org/wiki/Pixel_5a#Known_issues), or if you just upgraded to a new phone).

If this program skips the authenticator choice dialog when you don't want it to, for example, if you want to use a smartphone Bluetooth passkey only once or infrequently, you can hold <kbd>Shift</kbd> when the dialogs appear to temporarily suppress this program from automatically submitting the security key choice once.

Even if this program doesn't click the Next button (because an extra choice was present, or you were holding <kbd>Shift</kbd>), it will still highlight the Security Key option and focus the Next button for you, so you can just press <kbd>Enter</kbd> or <kbd>Space</kbd> to choose the Security Key anyway.

### 自动填 PIN / Auto-filling the security key PIN

**中文**：自 2025 年 9 月起，Windows 11 的更新要求 FIDO2 安全密钥在每次认证时都输入 PIN。如果你希望程序在缓存有效期内自动替你填入并提交 PIN，**右键系统托盘图标 → 点击「安全密钥 PIN」**（勾选表示已缓存），打开下方对话框：

<p align="center"><img src=".github/images/pin-en.png" alt="安全密钥 PIN 缓存对话框（English）" width="380" /> <img src=".github/images/pin-cn.png" alt="安全密钥 PIN 缓存对话框（中文）" width="380" /></p>

在对话框中输入你的安全密钥 PIN 并点击「缓存 PIN」（只通过对话框输入，不会出现在命令行或进程列表中）。PIN 会被**加密后缓存在内存中**（Windows `CryptProtectMemory`，进程级密钥，从不写入磁盘），默认 600 秒后过期——过期时长与失效时机均可在托盘菜单的「PIN 设置」中调整（详见[系统托盘图标](#系统托盘图标--system-tray-icon)）。该功能只适用于**单把安全密钥**——插着多把密钥时程序会拒绝缓存，以免把某把密钥的 PIN 输进另一把密钥导致其被锁死。要清除缓存的 PIN，再次打开该对话框并选择「清除已缓存 PIN」，或启用托盘菜单中的「锁屏 / 睡眠 / 休眠时失效」。注意：即使内存加密，任何能操作这台电脑的人仍可能在你使用密钥期间借它完成认证，请自行权衡（完整风险说明见[安全](#安全--security)一节的免责声明）。

高级（命令行）：如果你只想自己输入 PIN 后省去按回车，可传入 `--autosubmit-pin-length=$num`，程序会在你输入满 $num 个字符后自动提交该对话框（最少 4 位）。请谨慎输入——连续输错足够多次（YubiKey 为 8 次）会永久锁定安全密钥，直到你重置它并丢失全部 FIDO 凭据。该选项不会在注册新 FIDO 凭据、修改 PIN 或输入 Windows Hello PIN（Windows 会自行自动提交）时自动提交。

**English**: Since September 2025, Windows 11 updates require a FIDO2 security key to be unlocked with its PIN on every assertion. If you'd like the program to fill in and submit the PIN for you while it is cached, **right-click the system tray icon and choose "Security key PIN"** (checked when a PIN is cached) to open the dialog above.

Type your security key PIN in the dialog and click "Cache PIN" (it is only ever entered in the dialog, never on a command line). The PIN is cached **encrypted in memory** (Windows `CryptProtectMemory`, a per-process key, never written to disk) and expires after 600 seconds by default — both the expiry and the forget timing are adjustable in the tray menu's "PIN settings" (see [System tray icon](#系统托盘图标--system-tray-icon)). This only works with a **single security key** — if more than one key is attached, the program refuses to cache the PIN rather than risk entering one key's PIN into another and locking it out. To clear the cached PIN, open the dialog again and choose "Clear cached PIN", or enable "Forget on lock / sleep / hibernate" in the tray menu. Note that even encrypted in memory, anyone who can operate this computer as you could use your key while you are authenticated; weigh the trade-off before enabling it (see the disclaimer in the [Security](#安全--security) section for a fuller statement of the risks).

Advanced (command line): if you'd rather keep typing the PIN yourself but skip the final <kbd>Enter</kbd>, pass `--autosubmit-pin-length=$num`: when Windows shows the FIDO2 PIN prompt for your USB security key, the program auto-submits the dialog once you have typed $num characters (minimum 4). Type with care — enough consecutive wrong submissions (8 on YubiKeys) will permanently block the security key until you reset it and lose all its FIDO credentials. It will neither auto-submit when registering a new FIDO credential, changing your PIN, nor when entering a Windows Hello PIN (which Windows auto-submits on its own).

### 第三方 passkey 提供商（优先级文件）/ Choosing among third-party passkey providers (priority file)

**中文**：Windows 25H2 允许 1Password、Bitwarden、KeePass 等密码管理器注册为 passkey 提供商，从而在「选择通行密钥」弹窗中增加额外选项。默认情况下，本程序一旦发现某个选项既不是 USB 安全密钥、也不是配对手机，就会停止自动提交——因为它无法判断你是否更想使用密码管理器。

**最简单的方式**：托盘菜单的「首选验证方法」会自动列出系统中已注册的第三方 passkey 提供商，直接点选即可让程序优先使用它（详见[系统托盘图标](#系统托盘图标--system-tray-icon)）。

高级（优先级文件）：如需更精细的优先级规则，可在可执行文件旁创建 `priority.txt` 文件（或用 `--priority-file=PATH` 指向其他路径）。每行格式如下：

```
对话框中显示的选项名称 = 优先级数字
```

数字越大越优先。三个特殊键有固定含义：

| 键 | 含义 |
|-----|---------|
| `USB` | USB 安全密钥选项 |
| `Pair new phone` | 配对新的蓝牙手机 |
| `Use existing phone` | 已配对的蓝牙手机 |

其他键会与弹窗中显示的选项文本做大小写不敏感匹配，因此你可以按名称添加任何现有或未来的提供商。例如，在可用时优先选择 1Password，否则回退到 USB 安全密钥：

```
1Password = 200
USB = 100
```

或者让 USB 安全密钥始终优先于任何密码管理器：

```
USB = 200
1Password = 100
Bitwarden = 100
```

如果不配置任何规则，默认行为（优先 USB 安全密钥）保持不变。

**English**: Windows 25H2 lets password managers like 1Password, Bitwarden, KeePass, and others register as passkey providers, which adds their names as extra options in the "Choose a passkey" dialog. By default this program stops (does not auto-submit) whenever it sees an option that is neither the USB security key nor pairing a new phone, because it can't know whether you'd prefer the password manager.

**The simplest way**: the tray menu's "Preferred authenticator" automatically lists the third-party passkey providers registered on your system — just pick one to have the program prefer it (see [System tray icon](#系统托盘图标--system-tray-icon)).

Advanced (priority file): for finer-grained priority rules, create a `priority.txt` file next to the executable (or point to another path with `--priority-file=PATH`). Each line has the form:

```
Display name as it appears in the dialog = priority number
```

The higher the number, the more preferred. Three special keys have fixed meanings:

| Key | Meaning |
|-----|---------|
| `USB` | The USB security key option |
| `Pair new phone` | Pairing a new Bluetooth phone |
| `Use existing phone` | An already-paired Bluetooth phone |

Every other key is matched case-insensitively against the option text as shown in the dialog, so you can add any current or future provider by name. For example, to prefer 1Password when available and fall back to the USB security key otherwise:

```
1Password = 200
USB = 100
```

Or to always prefer the USB security key over any password manager:

```
USB = 200
1Password = 100
Bitwarden = 100
```

If no rules are configured, the default behavior (prefer the USB security key) is unchanged.

### 系统托盘图标 / System tray icon

**中文**：程序运行期间会在系统托盘中显示一个图标。右键点击它，可以：

<p align="center"><img src=".github/images/main-en.png" alt="系统托盘主菜单（English）" width="340" /> <img src=".github/images/main-cn.png" alt="系统托盘主菜单（中文）" width="340" /></p>

- **启用 / 禁用自动选择安全密钥** —— 禁用后，程序完全不触碰任何 FIDO 弹窗，当你需要手动选择其他认证器或密码管理器中保存的 passkey 时很有用。
- **首选验证方法（Preferred authenticator）** —— 子菜单中列出了本系统当前可用的验证方法（自动从系统获取），包括「默认（自动）」「USB 安全密钥」「配对手机」「使用已有手机」，以及系统中已注册的第三方 passkey 提供商（如 1Password、Bitwarden）。选择后，程序在 FIDO 弹窗出现时会优先选择该方法。与 `--priority-file` 配合使用时可覆盖其优先级。
- **语言（Language）** —— 子菜单中可切换界面语言，支持「跟随系统语言」、English、简体中文、繁體中文，切换后立即生效，无需重启。
- **开机自启（Start automatically at logon）** —— 勾选后在 Windows 登录时自动以最高权限启动本程序（创建计划任务）；取消勾选即移除。
- **PIN 缓存（PIN cache）** —— 一级菜单中的高频操作；勾选表示已缓存安全密钥 PIN，点击打开对话框缓存或清除 PIN（加密后仅存内存、不落盘，详见上文「自动填 PIN」）。
- **PIN 设置（PIN settings）** —— 一个分组子菜单：
  - **过期时间（TTL，Expiration）** —— 单选预设：5 分钟 / 10 分钟（默认）/ 30 分钟 / 1 小时 / 直到退出。
  - **锁屏时失效 / 睡眠时失效 / 休眠时失效（Forget on lock / sleep / hibernate）** —— 三个开关，勾选后分别在 Windows 锁屏、睡眠、休眠时自动清除缓存的 PIN。
- **退出（Exit）** —— 退出程序，作为在任务管理器中结束进程的替代方式。

以下偏好都会持久化到 `%APPDATA%\PasskeyPick\settings.json`，重启后自动恢复：界面语言、「自动密钥」开关、首选验证方法、PIN 缓存 TTL 与锁屏/睡眠/休眠失效开关。PIN 本身从不写入磁盘。

**English**: A system tray icon appears while the program is running. Right-click it to:

- **Enable / disable automatic security key selection** — when disabled, the program leaves all FIDO dialogs completely untouched, which is useful when you want to manually choose another authenticator or a passkey stored in a password manager.
- **Preferred authenticator** — this submenu lists the authentication methods currently available on this system (auto-enumerated from the system): "Default (automatic)", "USB security key", "Pair a new phone", "Use an existing phone", plus any third-party passkey providers registered on the system (such as 1Password, Bitwarden). Picking one makes the program prefer that method when a FIDO prompt appears.
- **Language** — this submenu switches the UI language at runtime with immediate effect, without restarting: "Follow system language", English, 简体中文, 繁體中文.
- **Start automatically at logon** — checked when the program is registered (as a scheduled task) to start at Windows logon with highest privileges; uncheck to remove it.
- **PIN cache** — a high-frequency action in the top-level menu; checked when a security key PIN is cached, and clicking it opens the dialog that caches or clears the PIN (encrypted in memory only, never on disk; see "Auto-filling the security key PIN" above).
- **PIN settings** — a grouped submenu:
  - **Expiration (TTL)** — a single-select preset: 5 minutes / 10 minutes (default) / 30 minutes / 1 hour / until exit.
  - **Forget on lock / sleep / hibernate** — three toggles that clear the cached PIN when Windows locks, sleeps, or hibernates.
- **Exit** — quits the program, as an alternative to ending it in Task Manager.

The following preferences are persisted to `%APPDATA%\PasskeyPick\settings.json` and restored after a restart: the UI language, the automatic-selection toggle, the preferred authenticator, and the PIN cache TTL and lock/sleep/hibernate toggles. The PIN itself is never written to disk.

<p align="center"><img src=".github/images/settings-en.png" alt="PIN 设置子菜单（English）" width="340" /> <img src=".github/images/settings-cn.png" alt="PIN 设置子菜单（中文）" width="340" /></p>

## 系统要求 / Requirements

**中文**：

- Windows 11 25H2、24H2、23H2，或 [22H2 Moment 4](https://support.microsoft.com/en-us/topic/september-26-2023-kb5030310-os-build-22621-2361-preview-363ac1ae-6ea8-41b3-b3cc-22a2a5682faf)
- [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime) 或更高版本，x64 或 arm64（界面为 WinForms 原生托盘）
- 通过远程桌面连接（Remote Desktop Connection）使用 Windows 时，本程序必须运行在客户端而非服务器上，因为 FIDO 弹窗会被转发并在 `mstsc` 窗口之外的客户端显示

**English**:

- Windows 11 25H2, 24H2, 23H2, or [22H2 Moment 4](https://support.microsoft.com/en-us/topic/september-26-2023-kb5030310-os-build-22621-2361-preview-363ac1ae-6ea8-41b3-b3cc-22a2a5682faf)
- [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime) or later, either x64 or arm64 (native WinForms tray UI)
- When using Windows over Remote Desktop Connection, this program must run on the client, not the server, because FIDO prompts are forwarded and displayed by the client outside of the `mstsc` window

## 安装 / Installation

**中文**：

1. [下载与你 CPU 架构对应的最新发布版 `PasskeyPick-win-x64.exe` 或 `PasskeyPick-win-arm64.exe`。](https://github.com/f1owkang/PasskeyPick/releases/latest)
1. 将下载的 exe 重命名为 `PasskeyPick.exe` 并放到你选择的目录，例如 `C:\Program Files\PasskeyPick\`。
    - 发布版为框架依赖的单文件（约 2 MB），首次运行前需安装 [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime)。
1. 双击 `PasskeyPick.exe` 运行程序。
    - 程序没有主窗口，运行后驻留在系统托盘中（右键托盘图标可打开设置菜单），但你可以通过任务管理器中搜索 `PasskeyPick` 来确认它在运行。
1. 若希望在登录 Windows 时自动运行，**右键系统托盘图标 → 勾选「开机自启」**（详见[系统托盘图标](#系统托盘图标--system-tray-icon)）。如果你还想指定额外的命令行参数（如 `--skip-all-non-security-key-options`），也可以在启动程序时一并传入。
    - 等效地，也可以在命令行运行一次 `.\PasskeyPick --autostart-on-logon`，或自行在任务计划程序中新建一个以你的用户身份、最高权限启动 `PasskeyPick.exe` 的任务。

**English**:

1. [Download `PasskeyPick-win-x64.exe` or `PasskeyPick-win-arm64.exe` for your CPU architecture.](https://github.com/f1owkang/PasskeyPick/releases/latest)
1. Rename the downloaded EXE to `PasskeyPick.exe` and save it to a directory of your choice, like `C:\Program Files\PasskeyPick\`.
    - The release is a framework-dependent single file (~2 MB); install the [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime) before the first run.
1. Run the program by double-clicking `PasskeyPick.exe`.
    - No main window will appear — the program runs in the system tray (right-click its icon for the settings menu) — but you can tell it's running by searching for `PasskeyPick` in Task Manager.
1. To start automatically when you log in to Windows, **right-click the tray icon and check "Start automatically at logon"** (see [System tray icon](#系统托盘图标--system-tray-icon)). If you'd like to specify additional command-line arguments like `--skip-all-non-security-key-options`, you can pass them when launching the program too.
    - Alternatively, run `.\PasskeyPick --autostart-on-logon` once on the command line, or create a Task Scheduler task that starts `PasskeyPick.exe` as your user with highest privileges.
    - Manually add a new task to Task Scheduler that starts `PasskeyPick.exe` as your user with highest privileges when you log in to Windows

## 安全 / Security

**中文**：本程序必须**始终以管理员权限运行**，才能与 Windows 安全中心（`CredentialUIBroker.exe`，以更高的完整性级别运行）的 FIDO 弹窗交互——这是 Windows 用户界面特权隔离（UIPI）的要求，无法在普通权限下工作。

由于程序以最高权限常驻后台，请把它部署到**受保护目录**（例如 `C:\Program Files\PasskeyPick\`），不要放在低权限用户可写的目录，否则低权限用户可能利用 DLL 搜索顺序劫持加载恶意 DLL，或篡改 `priority.txt` 把认证重定向到恶意提供商。程序在启动时会检查部署目录是否可被低权限用户写入，并在日志中警告；`priority.txt` 的属主若不是你本人或本地管理员，程序会忽略该文件。

程序只会与**微软签名、位于 System32 的系统进程**（`CredentialUIBroker.exe`、`Consent.exe` 等）持有的 FIDO 弹窗交互，其他进程无法伪造弹窗来窃取缓存的安全密钥 PIN。

**English**: This program must **always run as administrator** to interact with the Windows Security FIDO dialogs, which are hosted by `CredentialUIBroker.exe` at a higher integrity level — Windows User Interface Privilege Isolation (UIPI) requires this, and it cannot work unelevated.

Because it runs elevated in the background, install it in a **protected directory** (for example `C:\Program Files\PasskeyPick\`), not in a directory writable by lower-privileged users, or a lower-privileged user could hijack DLL search order to load a malicious DLL, or tamper with `priority.txt` to redirect authentication to a malicious provider. On startup the program checks whether its directory is writable by unprivileged users and warns in the log; a `priority.txt` whose owner is neither you nor a local administrator is ignored.

The program only interacts with FIDO dialogs owned by **Microsoft-signed system processes in System32** (`CredentialUIBroker.exe`, `Consent.exe`, etc.), so other processes cannot spoof a prompt to steal the cached security key PIN.

**免责声明 / Disclaimer**

**中文**：本程序（包括其安全密钥相关功能，如 PIN 缓存、自动填 PIN、自动提交 PIN）按「现状」提供，不附带任何明示或暗示的保证。**缓存 PIN 存在固有风险**：即使 PIN 仅加密保存在内存中、从不写入磁盘，任何能操作这台电脑的人或进程，都可能在你保持认证状态期间使用该缓存 PIN 完成未经授权的认证；自动填 PIN 或自动提交也可能因输入错误导致安全密钥被连续锁定。使用本程序即表示你理解并**自行承担全部风险**，包括但不限于未授权认证、认证失败、安全密钥被锁死、数据丢失或财产损失。**仓库作者对因使用本程序而产生的任何后果不承担责任。**

**English**: This program (including its security-key features such as PIN caching, PIN auto-fill, and PIN auto-submit) is provided "as is", without warranty of any kind, express or implied. **Caching a PIN carries inherent risk**: even though the PIN is only ever stored encrypted in memory and never written to disk, any person or process that can operate this computer could use the cached PIN to authenticate without your authorization while you remain authenticated; auto-filling or auto-submitting a PIN could also lock out the security key after too many wrong entries. By using this program you acknowledge and **assume all risk**, including but not limited to unauthorized authentication, authentication failures, a locked-out security key, data loss, or financial damage. **The repository author accepts no responsibility for any consequence of using this program.**

## 演示 / Demo

**中文**：想用示例 FIDO 认证弹窗测试，请访问 [WebAuthn.io](https://webauthn.io) 并点击 **Authenticate** 按钮。

**English**: To test with a sample FIDO authentication prompt, visit [WebAuthn.io](https://webauthn.io) and click the **Authenticate** button.

## 构建 / Building

**中文**：如果你想自己构建该应用而不是从[发布页](https://github.com/f1owkang/PasskeyPick/releases)下载预编译二进制，可以按以下步骤操作。

1. 安装[最新稳定版 .NET SDK](https://dotnet.microsoft.com/en-us/download)（10 或更高版本）。
1. 克隆本仓库。
    ```ps1
    git clone "https://github.com/f1owkang/PasskeyPick.git"
    ```
1. 进入项目目录。
    ```ps1
    cd .\PasskeyPick\PasskeyPick\
    ```
1. 选择要构建的[版本标签](https://github.com/f1owkang/PasskeyPick/tags)，或跳过此步以使用 `master` 分支的最新提交。
    ```sh
    git checkout 0.6.0
    ```
1. 构建并发布程序（`PublishSingleFile=true` 会生成单文件可执行文件，`SelfContained=false` 使发布为框架依赖模式，体积仅约 2 MB，但目标机需安装 [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime)）。
    ```ps1
    dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true
    ```

假设你的 CPU 架构是 x64，程序将发布为以下路径的单文件可执行文件。
```text
.\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\PasskeyPick.exe
```

如果你更喜欢 IDE，也可以使用 [Visual Studio](https://visualstudio.microsoft.com/vs/) Community 2022 或 2026 等集成开发环境。

**English**: If you want to build this application yourself instead of downloading precompiled binaries from the [releases](https://github.com/f1owkang/PasskeyPick/releases) page, you can follow these steps.

1. Install the [latest stable .NET SDK](https://dotnet.microsoft.com/en-us/download) (10 or later).
1. Clone this repository.
    ```ps1
    git clone "https://github.com/f1owkang/PasskeyPick.git"
    ```
1. Go to the project directory.
    ```ps1
    cd .\PasskeyPick\PasskeyPick\
    ```
1. Choose one of the [version tags](https://github.com/f1owkang/PasskeyPick/tags) to build, or skip this step to use the head commit on the `master` branch.
    ```sh
    git checkout 0.6.0
    ```
1. Build and publish the program (`PublishSingleFile=true` produces a single-file executable, and `SelfContained=false` makes the publish framework-dependent at only ~2 MB, but the target machine needs the [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime)).
    ```ps1
    dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true
    ```

The program will be published to the following path as a single-file executable, assuming your CPU architecture is x64.
```text
.\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\PasskeyPick.exe
```

You can also use an IDE like [Visual Studio](https://visualstudio.microsoft.com/vs/) Community 2022 or 2026 instead of the command line if you prefer.

## 相关 / Related

### 创建新 passkey / Creating new passkeys

**中文**：当网站强制要求新 passkey 只能保存在 TPM 或安全密钥上时，可安装 [**Create Passkeys Anywhere** 用户脚本](https://github.com/Aldaviva/userscripts/raw/master/create-passkeys-anywhere.user.js)（需要 [Tampermonkey](https://tampermonkey.net/) 等扩展，Windows 与 Android 浏览器均可用）。安装后每次创建 passkey 都会询问存储位置；也可编辑脚本源码中的 `options.allowedPasskeyCreationStorage`（`anywhere` / `securityKey` / `tpm`）来限定保存位置。

**English**: When a website forces a new passkey to be stored only in the TPM or only on a security key, install my [**Create Passkeys Anywhere** user script](https://github.com/Aldaviva/userscripts/raw/master/create-passkeys-anywhere.user.js) (requires [Tampermonkey](https://tampermonkey.net/) or similar; works on Windows and Android browsers). It then asks where to save each new passkey; you can also set `options.allowedPasskeyCreationStorage` in the script source (`anywhere` / `securityKey` / `tpm`) to restrict the destination.
