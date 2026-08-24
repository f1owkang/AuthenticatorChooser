<img src="PasskeyPick/YubiKey.ico" height="24" alt="YubiKey 5 NFC USB-A" /> PasskeyPick
===

[![Release](https://img.shields.io/github/v/release/f1owkang/PasskeyPick?logo=github)](https://github.com/f1owkang/PasskeyPick/releases/latest) [![Build status](https://img.shields.io/github/actions/workflow/status/f1owkang/PasskeyPick/dotnet.yml?logo=github)](https://github.com/f1owkang/PasskeyPick/actions/workflows/dotnet.yml) [![Download count](https://img.shields.io/github/downloads/f1owkang/PasskeyPick/total?logo=github)](https://github.com/f1owkang/PasskeyPick/releases)

*驻留系统托盘的后台程序：自动跳过 Windows FIDO/WebAuthn 弹窗中的「配对手机」步骤，直接选择「USB 安全密钥」。*
*A background program with a system tray icon that skips the phone pairing step in Windows FIDO/WebAuthn prompts and chooses the USB security key.*

**亮点 / Highlights**

- **自动选 USB 安全密钥**——蓝牙界面一闪而过，直达「插入你的 USB 安全密钥」/ auto-selects the USB security key, skipping the Bluetooth screen
- **可选 PIN 缓存与自动填写**——内存加密、TTL 过期、锁屏失效、调试器检测 / optional in-memory-encrypted PIN cache with auto-fill, TTL, lock-screen expiry and debugger detection
- **托盘 GUI 集中配置**——首选验证方法、语言、开机自启、GPG 转发 / tray GUI for preferred authenticator, language, autostart and GPG forwarding
- **GPG 桥**——通过 SSH `RemoteForward` 在远程机器上用本地 YubiKey 签名 / sign on remote machines with your local YubiKey over SSH `RemoteForward`

<!-- MarkdownTOC autolink="true" bracket="round" autoanchor="false" levels="1,2" -->

- [问题与方案 / Problem & Solution](#问题与方案--problem--solution)
- [系统要求 / Requirements](#系统要求--requirements)
- [安装 / Installation](#安装--installation)
- [安全 / Security](#安全--security)
- [构建 / Building](#构建--building)
- [相关 / Related](#相关--related)

<!-- /MarkdownTOC -->

## 问题与方案 / Problem & Solution

**中文**：Windows 11 [22H2 Moment 4](https://www.bleepingcomputer.com/news/microsoft/windows-11-moment-4-update-released-here-are-the-many-new-features/) 起，WebAuthn 认证会先弹出「选择通行密钥」步骤，选 USB 安全密钥需额外一次点击或三次按键，且无法跳过、不会被记住。本程序用 [Microsoft UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview) 监视 FIDO 凭据弹窗并自动代选「安全密钥」——从用户角度看，蓝牙界面几乎刚出现就被替换为「插入你的 USB 安全密钥」。

<p align="center"><img src=".github/images/usb-prompt.png" alt="usb security key prompt" width="456" /></p>

<p align="center"><img src=".github/images/demo.gif" alt="demo" width="465" /></p>

**English**: Since Windows 11 [22H2 Moment 4](https://www.bleepingcomputer.com/news/microsoft/windows-11-moment-4-update-released-here-are-the-many-new-features/), WebAuthn authentication opens with a "Choose a passkey" step where picking the USB security key costs an extra click or three keystrokes — it cannot be skipped or remembered. This program watches for FIDO credential prompts via [Microsoft UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview) and picks Security Key for you: the Bluetooth screen barely appears before it is replaced with "plug in your USB security key".

想用示例 FIDO 弹窗测试，访问 [WebAuthn.io](https://webauthn.io) 点 **Authenticate** / To test with a sample prompt, visit [WebAuthn.io](https://webauthn.io) and click **Authenticate**.

### 覆盖自动行为 / Overriding the automatic behavior

**中文**：默认不干预本地 TPM 通行密钥弹窗（Windows Hello PIN / 生物识别），也不自动提交含「USB 安全密钥」「配对新手机」以外选项的弹窗——它无法判断你是否更想用那些选项。想大多数情况优先 USB，在托盘「首选验证方法」选中即可；只有想在**所有**情况下强制选 USB 才用 `--skip-all-non-security-key-options`。

- 弹窗出现时按住 <kbd>Shift</kbd>，临时禁止自动提交一次。
- 即使不点「下一步」，它仍会高亮「安全密钥」并聚焦「下一步」，你按 <kbd>Enter</kbd> 即可。
- 移除已配对手机选项：编辑注册表取消配对。

**English**: By default it does not touch local TPM passkey prompts (Windows Hello PIN / biometrics) and does not auto-submit prompts offering choices beyond "USB security key" and "Pair a new phone" — it cannot know whether you prefer those. To prefer USB in most cases, pick it under **Preferred authenticator** in the tray menu; only use `--skip-all-non-security-key-options` to force it in **_all_** cases.

- Hold <kbd>Shift</kbd> when the dialogs appear to suppress auto-submission once.
- Even when it doesn't click Next, it highlights Security Key and focuses Next — just press <kbd>Enter</kbd>.
- To remove an already-paired phone option, edit the registry to unpair it.

### 自动填 PIN / Auto-filling the security key PIN

**中文**：自 2025 年 9 月起，Windows 11 要求 FIDO2 安全密钥每次认证都输入 PIN。想自动填入并提交，**右键托盘图标 →「PIN 缓存」**打开对话框：

<p align="center"><img src=".github/images/pin-en.png" alt="安全密钥 PIN 缓存对话框（English）" width="380" /> <img src=".github/images/pin-cn.png" alt="安全密钥 PIN 缓存对话框（中文）" width="380" /></p>

PIN 只通过对话框输入（从不出现在命令行），**加密后仅缓存在内存**（Windows `CryptProtectMemory`，进程级密钥，从不写盘），默认 120 秒过期（托盘「PIN 设置」可调，最长 10 分钟或直到退出）。仅适用于**单把安全密钥**（插多把时拒绝缓存，避免输错密钥导致锁死）；「锁屏 / 睡眠 / 休眠时失效」默认开启，检测到调试器附加时立即清空并拒绝解密。注意：即使内存加密，能操作这台电脑的人仍可能在认证期间借它完成认证，详见[免责声明](#免责声明--disclaimer)。

高级：`--autosubmit-pin-length=$num` 输满 $num 个字符（最少 4）后自动提交 PIN 弹窗。注意——连续输错足够多次（YubiKey 为 8 次）会永久锁定密钥；注册新凭据、修改 PIN 或输入 Windows Hello PIN 时不会自动提交。

**English**: Since September 2025, Windows 11 requires a FIDO2 security key to be unlocked with its PIN on every assertion. To auto-fill and submit it, **right-click the tray icon → "PIN cache"** to open the dialog above. The PIN is only ever typed into the dialog (never on a command line), cached **encrypted in memory** (Windows `CryptProtectMemory`, per-process key, never on disk), and expires after 120 seconds by default (adjustable in the tray menu, up to 10 minutes or until exit). It only works with a **single security key** (with several attached, caching is refused rather than risk locking out the wrong key); "forget on lock / sleep / hibernate" is on by default, and the cache is wiped and decryption refused the moment a debugger is detected. Note that anyone who can operate this computer could use your key while you are authenticated — see the [disclaimer](#免责声明--disclaimer).

Advanced: `--autosubmit-pin-length=$num` submits the PIN prompt once you have typed $num characters (minimum 4). Enough consecutive wrong submissions (8 on YubiKeys) permanently block the key; it never auto-submits when registering a credential, changing the PIN, or entering a Windows Hello PIN.

### 第三方 passkey 提供商 / Third-party passkey providers

**中文**：Windows 25H2 允许 1Password、Bitwarden 等注册为 passkey 提供商；发现这类选项时默认停止自动提交。**最简单的方式**是在托盘「首选验证方法」中点选。需要更细规则时，在可执行文件旁创建 `priority.txt`（或 `--priority-file=PATH`），每行 `选项名称 = 优先级数字`，越大越优先；内置键：`USB`、`Pair new phone`、`Use existing phone`，其他键与弹窗选项文本做大小写不敏感匹配：

```
1Password = 200
USB = 100
```

**English**: Windows 25H2 lets password managers like 1Password and Bitwarden register as passkey providers; by default the program stops auto-submitting when it sees such an option. **The simplest way** is to pick a provider from the tray menu. For finer rules, create `priority.txt` next to the executable (or `--priority-file=PATH`), one `Display name = priority` per line (higher wins); built-in keys: `USB`, `Pair new phone`, `Use existing phone` — other keys match the dialog option text case-insensitively (see example above).

### 系统托盘图标 / System tray icon

**中文**：右键托盘图标可使用全部功能：**启用/禁用自动选择**、**首选验证方法**、**语言**（跟随系统 / English / 简中 / 繁中，即时生效）、**开机自启**（以最高权限创建计划任务）、**检查更新**、**PIN 缓存**、**PIN 设置**（TTL 与锁屏/睡眠/休眠失效开关）、**GPG 设置 / 诊断**、**退出**。偏好持久化到 `%APPDATA%\PasskeyPick\settings.json`；PIN 本身从不写盘。

<p align="center"><img src=".github/images/main-en.png" alt="系统托盘主菜单（English）" width="340" /> <img src=".github/images/main-cn.png" alt="系统托盘主菜单（中文）" width="340" /></p>

**English**: The tray icon's right-click menu exposes everything: **enable/disable auto-selection**, **Preferred authenticator**, **Language** (follow system / English / 简中 / 繁中, instant), **Start at logon** (scheduled task, highest privileges), **Check for updates**, **PIN cache**, **PIN settings** (TTL plus lock/sleep/hibernate toggles), **GPG Settings / Diagnostics**, and **Exit**. Preferences persist to `%APPDATA%\PasskeyPick\settings.json`; the PIN itself is never written to disk.

<p align="center"><img src=".github/images/settings-en.png" alt="PIN 设置子菜单（English）" width="340" /> <img src=".github/images/settings-cn.png" alt="PIN 设置子菜单（中文）" width="340" /></p>

### GPG 桥与 gpg-agent 管理 / GPG bridge & gpg-agent management

**中文**：托盘 **GPG 设置**（唯一配置入口）提供两个默认关闭的开关——**GPG-agent 转发**（本地转发桥）与 **GPG-agent 守护**（登录拉起并每 30 秒保活）——以及**转发端口**（默认 `4321`）和实时运行状态；**GPG 诊断**一键生成可复制的诊断报告。

**通过 SSH 在远程机器上使用你的 YubiKey**：远程 `~/.ssh/config` 加一行（`<远程 socket>` 为远程 `gpgconf --list-dir agent-extra-socket` 的值，`<端口>` 与转发端口一致）：

```
RemoteForward <远程 socket> 127.0.0.1:<端口>
```

远程提交即由本地 YubiKey 托管的 gpg-agent 签名，GitHub 显示 **Verified** 徽标。PasskeyPick 以管理员权限运行时，守护以**中完整性**启动 gpg-agent（经受限计划任务），普通终端也能连接 `\\.\pipe\openssh-ssh-agent` 做 SSH 卡认证；代价是同用户的中完整性进程也能访问该代理——固有取舍。

> **安全说明（重要）**：桥只监听回环地址且默认关闭。但一旦启用，本机**任何进程**（包括其他 Windows 用户账户）都能通过它访问你的 gpg-agent 并代你签名/解密——**签名并不总是要求 PIN 确认**。这与 `ssh -A` 风险同类且暴露面更大。仅在可信的单用户机器上启用。

高级：`--gpg-bridge-port=$port` 覆盖转发端口（推荐在 GUI 中配置）。

**English**: Tray **GPG Settings** (the single configuration hub) offers two off-by-default toggles — **GPG-agent forwarding** (the local bridge) and **GPG-agent daemon** (start at logon, keep-alive every 30 s) — plus the **forwarding port** (default `4321`) and live status; **GPG Diagnostics** generates a one-click copyable report.

**Using your YubiKey over SSH**: add `RemoteForward <remote socket> 127.0.0.1:<port>` to the remote `~/.ssh/config` (`<remote socket>` from `gpgconf --list-dir agent-extra-socket` there, `<port>` matching the forwarding port). Remote commits are then signed by your local YubiKey-backed gpg-agent and show a **Verified** badge. When elevated, the daemon starts gpg-agent at **medium integrity** (via a limited scheduled task) so normal terminals can reach `\\.\pipe\openssh-ssh-agent`; the trade-off is that same-user medium-integrity processes can also reach the agent — inherent to this feature.

> **Security note (important)**: the bridge listens on loopback only and is off by default. But once enabled, **any local process** (including other Windows user accounts) can sign or decrypt through your gpg-agent — and **signing does not always require a PIN prompt**. Same class of risk as `ssh -A`, wider exposure. Enable only on a trusted, single-user machine.

Advanced: `--gpg-bridge-port=$port` overrides the port (the GUI is the recommended path).

## 系统要求 / Requirements

- Windows 11 25H2 / 24H2 / 23H2 / [22H2 Moment 4](https://support.microsoft.com/en-us/topic/september-26-2023-kb5030310-os-build-22621-2361-preview-363ac1ae-6ea8-41b3-b3cc-22a2a5682faf)
- [.NET Desktop Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime) 或更高，x64 或 arm64
- RDP 场景：程序必须运行在**客户端**而非服务器（FIDO 弹窗在客户端显示）/ over RDP, run on the **client**, not the server

## 安装 / Installation

**中文**：

1. [下载 `PasskeyPick-win-x64.exe` 或 `PasskeyPick-win-arm64.exe`](https://github.com/f1owkang/PasskeyPick/releases/latest)（框架依赖单文件，约 2 MB），重命名为 `PasskeyPick.exe` 放到受保护目录，如 `C:\Program Files\PasskeyPick\`。
1. 双击运行——无主窗口，驻留系统托盘；开机自启在托盘菜单勾选。

**English**:

1. [Download `PasskeyPick-win-x64.exe` or `PasskeyPick-win-arm64.exe`](https://github.com/f1owkang/PasskeyPick/releases/latest) (framework-dependent single file, ~2 MB), rename to `PasskeyPick.exe`, and place it in a protected directory like `C:\Program Files\PasskeyPick\`.
1. Double-click to run — no main window, it lives in the system tray; enable autostart from the tray menu.

## 安全 / Security

**中文**：本程序必须**始终以管理员权限运行**，才能与更高完整性级别的 `CredentialUIBroker.exe` FIDO 弹窗交互（Windows UIPI 的要求，普通权限无法工作）。因此请部署到**受保护目录**——低权限用户可写的目录会暴露 DLL 搜索顺序劫持与 `priority.txt` 篡改风险（启动时会检查并记入日志；属主不是本人或本地管理员的 `priority.txt` 会被忽略）。程序只与**微软签名、位于 System32 的系统进程**（`CredentialUIBroker.exe`、`Consent.exe` 等）持有的 FIDO 弹窗交互，且**填充 PIN 前会再次复核窗口属主**，其他进程无法伪造弹窗窃取缓存的安全密钥 PIN。

**English**: This program must **always run as administrator** to interact with the FIDO dialogs hosted by `CredentialUIBroker.exe` at a higher integrity level (required by Windows UIPI; it cannot work unelevated). Install it in a **protected directory** — a directory writable by lower-privileged users exposes DLL search-order hijacking and `priority.txt` tampering (checked at startup and logged; a `priority.txt` not owned by you or a local administrator is ignored). The program only interacts with FIDO dialogs owned by **Microsoft-signed system processes in System32** (`CredentialUIBroker.exe`, `Consent.exe`, etc.) and **re-verifies the window owner immediately before filling a PIN**, so other processes cannot spoof a prompt to steal the cached security key PIN.

### 免责声明 / Disclaimer

**中文**：本程序（包括 PIN 缓存、自动填 PIN、自动提交 PIN）按「现状」提供，不附带任何保证。**缓存 PIN 存在固有风险**：即使 PIN 仅加密保存在内存、从不写盘，任何能操作这台电脑的人或进程，都可能在你保持认证状态期间用该缓存 PIN 完成未授权认证；自动填/自动提交也可能因输错导致安全密钥被连续锁定。使用本程序即表示你理解并**自行承担全部风险**，包括但不限于未授权认证、认证失败、安全密钥被锁死、数据丢失或财产损失。**仓库作者对因使用本程序而产生的任何后果不承担责任。**

**English**: This program (including PIN caching, PIN auto-fill, and PIN auto-submit) is provided "as is", without warranty of any kind. **Caching a PIN carries inherent risk**: even though the PIN is only ever stored encrypted in memory, anyone who can operate this computer could use it to authenticate without your authorization while you remain authenticated; auto-filling or auto-submitting could also lock out the security key after too many wrong entries. By using this program you **assume all risk**, including but not limited to unauthorized authentication, authentication failures, a locked-out security key, data loss, or financial damage. **The repository author accepts no responsibility for any consequence of using this program.**

## 构建 / Building

**中文**：安装 [.NET SDK 10+](https://dotnet.microsoft.com/en-us/download)，克隆后发布（框架依赖单文件，约 2 MB）：

```ps1
git clone "https://github.com/f1owkang/PasskeyPick.git"; cd .\PasskeyPick
dotnet publish .\PasskeyPick -c Release -r win-x64 -p:PublishSingleFile=true
```

产物：`PasskeyPick\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\PasskeyPick.exe`（arm64 换 `-r win-arm64`）。

**English**: Install the [.NET SDK 10+](https://dotnet.microsoft.com/en-us/download), clone, and publish (framework-dependent single file, ~2 MB) with the commands above. Output: `PasskeyPick\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\PasskeyPick.exe` (use `-r win-arm64` for arm64).

## 相关 / Related

**中文**：网站强制新 passkey 只能存在 TPM 或安全密钥上时，可用 [**Create Passkeys Anywhere** 用户脚本](https://github.com/Aldaviva/userscripts/raw/master/create-passkeys-anywhere.user.js)（需 Tampermonkey 等扩展）每次创建时询问存储位置。

**English**: When a website forces new passkeys onto the TPM or a security key, the [**Create Passkeys Anywhere** userscript](https://github.com/Aldaviva/userscripts/raw/master/create-passkeys-anywhere.user.js) (needs Tampermonkey or similar) asks where to store each passkey at creation time.
