<img src="AuthenticatorChooser/YubiKey.ico" height="24" alt="YubiKey 5 NFC USB-A" /> AuthenticatorChooser
===

[![Download count](https://img.shields.io/github/downloads/Aldaviva/AuthenticatorChooser/total?logo=github)](https://github.com/Aldaviva/AuthenticatorChooser/releases) [![Build status](https://img.shields.io/github/actions/workflow/status/Aldaviva/AuthenticatorChooser/dotnet.yml?branch=master&logo=github)](https://github.com/Aldaviva/AuthenticatorChooser/actions/workflows/dotnet.yml)

*后台程序，自动跳过「配对手机」选项，并在 Windows FIDO/WebAuthn 弹窗中自动选择「USB 安全密钥」。*
*Background program that skips the phone pairing option and chooses the USB security key in Windows FIDO/WebAuthn prompts.*

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

**中文**：当某个程序（例如支持 WebAuthn 的浏览器）请求认证时，Windows 可能会显示 Windows 安全凭据提示，允许你使用 FIDO 认证器完成身份验证，例如 USB 安全密钥，或保存在电脑 TPM 中、由 Windows Hello PIN 或指纹等生物识别信息保护的通配密钥。

在 Windows 10 与 11（22H2 Moment 4 之前，即 2023 年 9 月之前），如果 TPM 中保存了向依赖方（如网站）完成认证所需的私钥，Windows 会优先要求用户输入该 TPM 认证器的质询（如 PIN 或指纹）；同时仍提供一个额外点击即可选择其他认证器（如 USB 安全密钥）的选项。反之，如果 TPM 中没有所需密钥，Windows 会立即提示你插入 USB 安全密钥。

<p align="center"><img src=".github/images/usb-prompt.png" alt="usb security key prompt" width="456" /></p> 

在 Windows 11 [22H2 Moment 4](https://www.bleepingcomputer.com/news/microsoft/windows-11-moment-4-update-released-here-are-the-many-new-features/)（2023 年 9 月）及更高版本（包括 [23H2](https://www.bleepingcomputer.com/news/microsoft/windows-11-23h2-new-features-in-the-windows-11-2023-update/)）中，行为发生了变化：现在可以通过蓝牙配对 Android 和 iOS 设备来使用其上的通配密钥，这在一定程度上缓解了通配密钥无法随 TPM 之外携带的问题。如果 Windows TPM 中含有该通配密钥，行为不变；但如果本地 TPM 中没有该密钥，则在插入 USB 安全密钥之前，会额外增加一个「使用密钥登录 / 选择通行密钥」步骤。

现在弹窗会显示「选择通行密钥」，你必须指明想使用「iPhone、iPad 或 Android 设备」还是「安全密钥」。选择 USB 安全密钥需要额外的一次点击或三次按键。即使你关闭蓝牙、没有 Android 或 iOS 设备、也从未想在本 Windows 电脑上使用它进行 FIDO 认证，也无法跳过这个新弹窗。Windows 同样不会记住你上次的选择。你也可以在设备管理器中禁用蓝牙设备，但这同样会禁用电脑上的其他蓝牙外设（如蓝牙鼠标、键盘、耳机、音箱）。

<p align="center"><img src=".github/images/authenticator-prompt.png" alt="authenticator prompt" width="456" /></p>

**English**: Windows can display a Windows Security credential prompt when requested by a program, such as a browser with WebAuthn. This allows you to authenticate using a FIDO authenticator, such as a USB security key or a passkey in your computer's TPM protected by a Windows Hello PIN or biometrics, like a fingerprint.

In Windows 10 and 11 prior to 22H2 Moment 4 (September 2023), if the TPM contains the private key needed to authenticate to the relying party (like a website), Windows will prioritize prompting for the user's challenge (like a PIN or fingerprint) for this TPM authenticator first. Windows will still provide an option to choose a different authenticator (like a USB security key) with an additional click. Otherwise, if the TPM does not contain the required secret, Windows will immediately prompt you to insert a USB security key.

In Windows 11 [22H2 Moment 4](https://www.bleepingcomputer.com/news/microsoft/windows-11-moment-4-update-released-here-are-the-many-new-features/) (September 2023) and later (including [23H2](https://www.bleepingcomputer.com/news/microsoft/windows-11-23h2-new-features-in-the-windows-11-2023-update/)), this behavior changed to include the ability to pair with Android and iOS devices over Bluetooth to use their passkeys, which somewhat ameliorates the problem of passkeys not being portable outside their TPM. The behavior is unchanged if the Windows TPM contains the passkey. However, if the local TPM does not contain the passkey, an additional "Sign in with your passkey"/"Choose a passkey" step was added before you can use your USB security key.

Now it says "Choose a passkey," and you have to indicate whether you want to use an "iPhone, iPad, or Android device" or a "Security key." Choosing the USB security key requires one additional click or three additional keystrokes. It is impossible to opt out of this new prompt, even if you turn off Bluetooth, don't have an Android or iOS device, or never want to use it for FIDO authentication on your Windows computer. Windows does not remember the most recently used choice, either. You could disable your Bluetooth device in Device Manager, but this will also prevent you from using any other Bluetooth peripherals with your computer, such as Bluetooth mice, keyboards, headphones, and speakers.

## 解决方案 / Solution

**中文**：这是一个在 Windows 用户会话中无界面后台运行的程序。它等待 Windows FIDO 凭据提供程序弹窗出现，然后自动为你选择「安全密钥」选项。从用户的角度看，蓝牙界面几乎刚出现就被替换为「插入你的 USB 安全密钥」的提示。

<p align="center"><img src=".github/images/demo.gif" alt="demo" width="465" /></p>     

在内部，本程序使用 [Microsoft UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview) 来读取并操作这些对话框。

**English**: This is a background program that runs headlessly in your Windows user session. It waits for Windows FIDO credential provider prompts to appear, then chooses the Security Key option for you automatically. From the user's perspective, the Bluetooth screen barely even appears before it's replaced with the prompt to plug in your USB security key.

Internally, this program uses [Microsoft UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview) to read and interact with the dialog boxes.

### 覆盖自动行为 / Overriding the automatic next behavior

**中文**：默认情况下，本程序不会干预本地 TPM 通配密钥弹窗（例如请求输入 Windows Hello PIN 或生物识别）。如果 FIDO 弹窗中包含除「USB 安全密钥」和「配对新的蓝牙手机」以外的其他选项——例如你已配对过手机，或你之前拒绝过 PIN 等 Windows Hello 因子而现在想从认证器选择弹窗中再次尝试 PIN——程序也不会自动提交。不过，如果你希望强制在所有情况下都选择 USB 安全密钥，即使还有其他有效选项（如 Windows Hello PIN/生物识别），可以在启动本程序时传入命令行参数 `--skip-all-non-security-key-options`（如果想改自动启动配置，参见[安装](#安装--installation)一节中推荐的自动启动方式）。

如果对话框中出现已配对的手机选项而你想移除它，[可以编辑注册表取消配对已有手机](https://github.com/Aldaviva/AuthenticatorChooser/wiki/Unpairing-Bluetooth-smartphone)。当你的旧手机[变砖](https://en.wikipedia.org/wiki/Pixel_5a#Known_issues)，或刚换新手机时，这会很有用。

如果本程序在你不想让它跳过的时候跳过了认证器选择弹窗（例如你只是想偶尔用一次手机蓝牙通配密钥），你可以在弹窗出现时按住 <kbd>Shift</kbd>，临时禁止本程序自动提交一次安全密钥选择。

即使本程序没有点击「下一步」按钮（因为存在额外选项，或你正按住 <kbd>Shift</kbd>），它仍会高亮「安全密钥」选项并聚焦「下一步」按钮，因此你只需按 <kbd>Enter</kbd> 或 <kbd>Space</kbd> 即可选择安全密钥。

**English**: By default, this program does not interfere with local TPM passkey prompts (like requesting your Windows Hello PIN or biometrics). It also does not automatically submit FIDO prompts that contain additional options besides a USB security key and pairing a new Bluetooth smartphone, such as the cases when you already have a paired phone, or you previously declined a Windows Hello factor like a PIN but want to try a PIN again from the authenticator choice dialog. However, you may override this behavior if you wish and force it to **_always_** choose the USB security key in all cases, even if there are other valid options like Windows Hello PIN/biometrics, by passing the command-line argument `--skip-all-non-security-key-options` when starting this program (see [Installation](#安装--installation) for the recommended autostart paths if you want to change it there).

If a paired phone option appears in the dialog box and you want to remove it, [you can edit the registry to unpair an existing phone](https://github.com/Aldaviva/AuthenticatorChooser/wiki/Unpairing-Bluetooth-smartphone). This is useful if your old phone [bricked itself](https://en.wikipedia.org/wiki/Pixel_5a#Known_issues), or if you just upgraded to a new phone.

If this program skips the authenticator choice dialog when you don't want it to, for example, if you want to use a smartphone Bluetooth passkey only once or infrequently, you can hold <kbd>Shift</kbd> when the dialogs appear to temporarily suppress this program from automatically submitting the security key choice once.

Even if this program doesn't click the Next button (because an extra choice was present, or you were holding <kbd>Shift</kbd>), it will still highlight the Security Key option and focus the Next button for you, so you can just press <kbd>Enter</kbd> or <kbd>Space</kbd> to choose the Security Key anyway.

### 自动填 PIN / Auto-filling the security key PIN

**中文**：自 2025 年 9 月起，Windows 11 的更新要求 FIDO2 安全密钥在每次认证时都输入 PIN。如果你希望程序在缓存有效期内自动替你填入并提交 PIN，可以**右键系统托盘图标 → 点击「安全密钥 PIN」**（勾选表示已缓存），或在命令行运行一次：

```ps1
.\AuthenticatorChooser --set-pin
```

两者都会打开同一个对话框，在其中输入你的安全密钥 PIN（只通过对话框输入，不会出现在命令行或进程列表中）。PIN 会被**加密后缓存在内存中**（使用 Windows `CryptProtectMemory`，进程级密钥，内存中不保留明文；也从不写入磁盘），并在 `--pin-cache-ttl` 秒内有效（默认 600 秒，参考 gpg-agent 的 `default-cache-ttl`）；传 `--pin-cache-ttl=0` 表示缓存一直有效直到程序重启。由于只存内存，程序每次重启后都需要重新缓存。该功能只适用于**单把安全密钥**——如果检测到插着多把密钥，程序会拒绝缓存，以免把某把密钥的 PIN 输进另一把密钥导致其被锁死。要清除缓存的 PIN，再次打开该对话框并选择「清除已缓存 PIN」。此外，你还可以传 `--pin-clear-on-lock`、`--pin-clear-on-sleep`、`--pin-clear-on-hibernate`，让程序分别在 Windows 锁屏、睡眠、休眠时自动清除缓存的 PIN（睡眠与休眠在系统层面都是同一个挂起事件，因此这两个选项行为相同），程序退出时也会自动清零缓存。注意：即使内存加密，任何能操作这台电脑的人仍可能在你使用密钥期间借它完成认证，请自行权衡（更完整的风险说明见[安全](#安全--security)一节的免责声明）。

另外，如果你不想让程序替你填写 PIN，而只希望自己输入完成后省去按回车，可以传入 `--autosubmit-pin-length=$num`：当 Windows 弹出安全密钥的 FIDO2 PIN 输入框时，程序会在你输入满 $num 个字符后自动提交该对话框（最少 4 位）。请谨慎输入——连续输错足够多次（YubiKey 为 8 次）会永久锁定安全密钥，直到你重置它并丢失全部 FIDO 凭据。该选项不会在注册新 FIDO 凭据、修改 PIN 或输入 Windows Hello PIN（Windows 会自行自动提交）时自动提交。

**English**: Since September 2025, Windows 11 updates require a FIDO2 security key to be unlocked with its PIN on every assertion. If you'd like the program to fill in and submit the PIN for you while it is cached, **right-click the system tray icon and choose "Security key PIN"** (checked when a PIN is cached), or run this once on the command line:

```ps1
.\AuthenticatorChooser --set-pin
```

Both open the same dialog, where you type your security key PIN (it is only ever entered in the dialog, never on a command line). The PIN is cached **encrypted in memory** (Windows `CryptProtectMemory`, a per-process key, so the plaintext never sits in memory, and it is never written to disk), valid for `--pin-cache-ttl` seconds (default 600 s, mirroring gpg-agent's `default-cache-ttl`); pass `--pin-cache-ttl=0` to keep it valid until the program restarts. Because it is memory-only, you must cache the PIN again after every restart. This only works with a **single security key** — if more than one key is attached, the program refuses to cache the PIN rather than risk entering one key's PIN into another and locking it out. To clear the cached PIN, open the dialog again and choose "Clear cached PIN". You can also pass `--pin-clear-on-lock`, `--pin-clear-on-sleep`, or `--pin-clear-on-hibernate` to have the program automatically forget the cached PIN when Windows locks, sleeps, or hibernates (sleep and hibernation both surface as the same suspend event, so those two options behave identically), and the cache is zeroed when the program exits. Note that even encrypted in memory, anyone who can operate this computer as you could use your key while you are authenticated; weigh the trade-off before enabling it (see the disclaimer in the [Security](#安全--security) section for a fuller statement of the risks).

Separately, if you'd rather keep typing the PIN yourself but skip the final <kbd>Enter</kbd>, pass `--autosubmit-pin-length=$num`: when Windows shows the FIDO2 PIN prompt for your USB security key, the program auto-submits the dialog once you have typed $num characters (minimum 4). Type with care — enough consecutive wrong submissions (8 on YubiKeys) will permanently block the security key until you reset it and lose all its FIDO credentials. It will neither auto-submit when registering a new FIDO credential, changing your PIN, nor when entering a Windows Hello PIN (which Windows auto-submits on its own).

### 第三方 passkey 提供商（优先级文件）/ Choosing among third-party passkey providers (priority file)

**中文**：Windows 25H2 允许 1Password、Bitwarden、KeePass 等密码管理器注册为 passkey 提供商，从而在「选择通行密钥」弹窗中增加额外选项。默认情况下，本程序一旦发现某个选项既不是 USB 安全密钥、也不是配对手机，就会停止自动提交——因为它无法判断你是否更想使用密码管理器。

要指定优先选择哪个选项，请在可执行文件旁创建 `priority.txt` 文件（或用 `--priority-file=PATH` 指向其他路径）。每行格式如下：

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

To choose which option to prefer, create a `priority.txt` file next to the executable (or point to another path with `--priority-file=PATH`). Each line has the form:

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

- **启用 / 禁用自动选择安全密钥** —— 禁用后，程序完全不触碰任何 FIDO 弹窗，当你需要手动选择其他认证器或密码管理器中保存的 passkey 时很有用。
- **首选验证方法（Preferred authenticator）** —— 子菜单中列出了本系统当前可用的验证方法（自动从系统获取），包括「默认（自动）」「USB 安全密钥」「配对手机」「使用已有手机」，以及系统中已注册的第三方 passkey 提供商（如 1Password、Bitwarden）。选择后，程序在 FIDO 弹窗出现时会优先选择该方法。与 `--priority-file` 配合使用时可覆盖其优先级。
- **语言（Language）** —— 子菜单中可切换界面语言，支持「跟随系统语言」、English、简体中文、繁體中文，切换后立即生效，无需重启。
- **开机自启（Start automatically at logon）** —— 勾选后在 Windows 登录时自动以最高权限启动本程序（创建计划任务）；取消勾选即移除。
- **PIN 缓存（PIN cache）** —— 一级菜单中的高频操作；勾选表示已缓存安全密钥 PIN，点击打开对话框缓存或清除 PIN（加密后仅存内存、不落盘，详见上文「自动填 PIN」）。
- **PIN 设置（PIN settings）** —— 一个分组子菜单：
  - **过期时间（TTL，Expiration）** —— 单选预设：5 分钟 / 10 分钟（默认）/ 30 分钟 / 1 小时 / 直到退出。
  - **锁屏时失效 / 睡眠时失效 / 休眠时失效（Forget on lock / sleep / hibernate）** —— 三个开关，勾选后分别在 Windows 锁屏、睡眠、休眠时自动清除缓存的 PIN。
- **退出（Exit）** —— 退出程序，作为在任务管理器中结束进程的替代方式。

以下偏好都会持久化到 `%APPDATA%\AuthenticatorChooser\settings.json`，重启后自动恢复：界面语言、「自动密钥」开关、首选验证方法、PIN 缓存 TTL 与锁屏/睡眠/休眠失效开关。PIN 本身从不写入磁盘。

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

The following preferences are persisted to `%APPDATA%\AuthenticatorChooser\settings.json` and restored after a restart: the UI language, the automatic-selection toggle, the preferred authenticator, and the PIN cache TTL and lock/sleep/hibernate toggles. The PIN itself is never written to disk.

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

1. [下载与你 CPU 架构对应的最新发布版 ZIP 压缩包。](https://github.com/Aldaviva/AuthenticatorChooser/releases/latest)
1. 将压缩包中的 `AuthenticatorChooser.exe` 解压到你选择的目录，例如 `C:\Program Files\AuthenticatorChooser\`。
    - 发布版为框架依赖的单文件（约 2 MB），首次运行前需安装 [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime)。
1. 双击 `AuthenticatorChooser.exe` 运行程序。
    - 由于它是没有界面的后台程序，不会显示任何窗口，但你可以在任务管理器中搜索 `AuthenticatorChooser` 来确认它在运行。
1. 使用**以下任意一种**方式注册程序在用户登录时自动运行。如果你还想指定额外的[命令行参数](https://github.com/Aldaviva/AuthenticatorChooser/wiki/Command%E2%80%90line-arguments)（如 `--skip-all-non-security-key-options`），也可以在这里一并设置。
    - 用 `--autostart-on-logon` 参数运行一次本程序
        ```ps1
        .\AuthenticatorChooser --autostart-on-logon
        ```
    - 在任务计划程序中手动新建一个任务，在登录 Windows 时以你的用户身份、最高权限启动 `AuthenticatorChooser.exe`

**English**:

1. [Download the latest release ZIP archive for your CPU architecture.](https://github.com/Aldaviva/AuthenticatorChooser/releases/latest)
1. Extract the `AuthenticatorChooser.exe` file from the ZIP archive to a directory of your choice, like `C:\Program Files\AuthenticatorChooser\`.
    - The release is a framework-dependent single file (~2 MB); install the [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime) before the first run.
1. Run the program by double-clicking `AuthenticatorChooser.exe`.
    - Nothing will appear because it's a background program with no UI, but you can tell it's running by searching for `AuthenticatorChooser` in Task Manager.
1. Register the program to run automatically on user logon with **any one** of the following techniques. If you'd like to specify additional [command-line arguments](https://github.com/Aldaviva/AuthenticatorChooser/wiki/Command%E2%80%90line-arguments) like `--skip-all-non-security-key-options`, you can do that here too.
    - Run this program once with the `--autostart-on-logon` argument
        ```ps1
        .\AuthenticatorChooser --autostart-on-logon
        ```
    - Manually add a new task to Task Scheduler that starts `AuthenticatorChooser.exe` as your user with highest privileges when you log in to Windows

## 安全 / Security

**中文**：本程序必须**始终以管理员权限运行**，才能与 Windows 安全中心（`CredentialUIBroker.exe`，以更高的完整性级别运行）的 FIDO 弹窗交互——这是 Windows 用户界面特权隔离（UIPI）的要求，无法在普通权限下工作。

由于程序以最高权限常驻后台，请把它部署到**受保护目录**（例如 `C:\Program Files\AuthenticatorChooser\`），不要放在低权限用户可写的目录，否则低权限用户可能利用 DLL 搜索顺序劫持加载恶意 DLL，或篡改 `priority.txt` 把认证重定向到恶意提供商。程序在启动时会检查部署目录是否可被低权限用户写入，并在日志中警告；`priority.txt` 的属主若不是你本人或本地管理员，程序会忽略该文件。

程序只会与**微软签名、位于 System32 的系统进程**（`CredentialUIBroker.exe`、`Consent.exe` 等）持有的 FIDO 弹窗交互，其他进程无法伪造弹窗来窃取缓存的安全密钥 PIN。

**English**: This program must **always run as administrator** to interact with the Windows Security FIDO dialogs, which are hosted by `CredentialUIBroker.exe` at a higher integrity level — Windows User Interface Privilege Isolation (UIPI) requires this, and it cannot work unelevated.

Because it runs elevated in the background, install it in a **protected directory** (for example `C:\Program Files\AuthenticatorChooser\`), not in a directory writable by lower-privileged users, or a lower-privileged user could hijack DLL search order to load a malicious DLL, or tamper with `priority.txt` to redirect authentication to a malicious provider. On startup the program checks whether its directory is writable by unprivileged users and warns in the log; a `priority.txt` whose owner is neither you nor a local administrator is ignored.

The program only interacts with FIDO dialogs owned by **Microsoft-signed system processes in System32** (`CredentialUIBroker.exe`, `Consent.exe`, etc.), so other processes cannot spoof a prompt to steal the cached security key PIN.

**免责声明 / Disclaimer**

**中文**：本程序（包括其安全密钥相关功能，如 PIN 缓存、自动填 PIN、自动提交 PIN）按「现状」提供，不附带任何明示或暗示的保证。**缓存 PIN 存在固有风险**：即使 PIN 仅加密保存在内存中、从不写入磁盘，任何能操作这台电脑的人或进程，都可能在你保持认证状态期间使用该缓存 PIN 完成未经授权的认证；自动填 PIN 或自动提交也可能因输入错误导致安全密钥被连续锁定。使用本程序即表示你理解并**自行承担全部风险**，包括但不限于未授权认证、认证失败、安全密钥被锁死、数据丢失或财产损失。**仓库作者对因使用本程序而产生的任何后果不承担责任。**

**English**: This program (including its security-key features such as PIN caching, PIN auto-fill, and PIN auto-submit) is provided "as is", without warranty of any kind, express or implied. **Caching a PIN carries inherent risk**: even though the PIN is only ever stored encrypted in memory and never written to disk, any person or process that can operate this computer could use the cached PIN to authenticate without your authorization while you remain authenticated; auto-filling or auto-submitting a PIN could also lock out the security key after too many wrong entries. By using this program you acknowledge and **assume all risk**, including but not limited to unauthorized authentication, authentication failures, a locked-out security key, data loss, or financial damage. **The repository author accepts no responsibility for any consequence of using this program.**

## 演示 / Demo

**中文**：想用示例 FIDO 认证弹窗测试，请访问 [WebAuthn.io](https://webauthn.io) 并点击 **Authenticate** 按钮。

**English**: To test with a sample FIDO authentication prompt, visit [WebAuthn.io](https://webauthn.io) and click the **Authenticate** button.

## 构建 / Building

**中文**：如果你想自己构建该应用而不是从[发布页](https://github.com/Aldaviva/AuthenticatorChooser/releases)下载预编译二进制，可以按以下步骤操作。

1. 安装[最新稳定版 .NET SDK](https://dotnet.microsoft.com/en-us/download)（10 或更高版本）。
1. 克隆本仓库。
    ```ps1
    git clone "https://github.com/Aldaviva/AuthenticatorChooser.git"
    ```
1. 进入项目目录。
    ```ps1
    cd .\AuthenticatorChooser\AuthenticatorChooser\
    ```
1. 选择要构建的[版本标签](https://github.com/Aldaviva/AuthenticatorChooser/tags)，或跳过此步以使用 `master` 分支的最新提交。
    ```sh
    git checkout 0.6.0
    ```
1. 构建并发布程序（`PublishSingleFile=true` 会生成单文件可执行文件，`SelfContained=false` 使发布为框架依赖模式，体积仅约 2 MB，但目标机需安装 [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime)）。
    ```ps1
    dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true
    ```

假设你的 CPU 架构是 x64，程序将发布为以下路径的单文件可执行文件。
```text
.\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\AuthenticatorChooser.exe
```

如果你更喜欢 IDE，也可以使用 [Visual Studio](https://visualstudio.microsoft.com/vs/) Community 2022 或 2026 等集成开发环境。

**English**: If you want to build this application yourself instead of downloading precompiled binaries from the [releases](https://github.com/Aldaviva/AuthenticatorChooser/releases) page, you can follow these steps.

1. Install the [latest stable .NET SDK](https://dotnet.microsoft.com/en-us/download) (10 or later).
1. Clone this repository.
    ```ps1
    git clone "https://github.com/Aldaviva/AuthenticatorChooser.git"
    ```
1. Go to the project directory.
    ```ps1
    cd .\AuthenticatorChooser\AuthenticatorChooser\
    ```
1. Choose one of the [version tags](https://github.com/Aldaviva/AuthenticatorChooser/tags) to build, or skip this step to use the head commit on the `master` branch.
    ```sh
    git checkout 0.6.0
    ```
1. Build and publish the program (`PublishSingleFile=true` produces a single-file executable, and `SelfContained=false` makes the publish framework-dependent at only ~2 MB, but the target machine needs the [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime)).
    ```ps1
    dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true
    ```

The program will be published to the following path as a single-file executable, assuming your CPU architecture is x64.
```text
.\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\AuthenticatorChooser.exe
```

You can also use an IDE like [Visual Studio](https://visualstudio.microsoft.com/vs/) Community 2022 or 2026 instead of the command line if you prefer.

## 相关 / Related

### 创建新 passkey / Creating new passkeys

**中文**：当你在浏览器中尝试创建 passkey 时，网站可能会强制要求只保存在 TPM 或只保存在安全密钥上，而不是让你自由选择两种存储位置。要覆盖网站的限制、让你重新掌控新 passkey 的保存位置，可以安装 [**Create Passkeys Anywhere** 用户脚本](https://github.com/Aldaviva/userscripts/raw/master/create-passkeys-anywhere.user.js)（需要 [Tampermonkey](https://tampermonkey.net/) 或类似的浏览器扩展）。它不只支持 Windows，例如在 Firefox for Android 上同样可用。

安装该脚本后，默认情况下每次创建新 passkey 时都会询问你要保存在安全密钥上还是 TPM 中。你也可以通过编辑脚本源码中的 `options.allowedPasskeyCreationStorage` 值来覆盖此行为：若将其从 `anywhere` 改为 `securityKey`，则只允许把新 passkey 保存在安全密钥上；若改为 `tpm`，则只允许保存在 TPM 中。

**English**: When you try to create a passkey in your browser, the website may force it to be stored only in the TPM or only on a security key, rather than letting you freely choose between the two destinations. To override the site's mandate and put yourself back in control of where your new passkey will be saved, you can install my [**Create Passkeys Anywhere** user script](https://github.com/Aldaviva/userscripts/raw/master/create-passkeys-anywhere.user.js) (requires [Tampermonkey](https://tampermonkey.net/) or a similar browser extension). It doesn't only run on Windows, for example it also works on Firefox for Android.

With this script installed, you will by default always be asked whether to save each new passkey on a security key or in the TPM. If you want to override this behavior, you can also configure the user script by editing the `options.allowedPasskeyCreationStorage` value in the script source. If you change it from `anywhere` to `securityKey`, it will only allow you to save new passkeys on security keys, and if you change it to `tpm`, it will only allow them to be saved in the TPM.
