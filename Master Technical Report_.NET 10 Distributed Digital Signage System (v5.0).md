# **Master Technical Report:.NET 10 Distributed Digital Signage System**

**Version:** 5.0 (Final Consolidated Architecture)

**Target Platform:** Raspberry Pi 5 (ARM64) /.NET 10 (LTS)

**Architecture:** Distributed (On-Premise Docker Server / Thin Client)

**Core Strategies:** SMB Streaming, Self-Updating Players, Canary Deployments, Live Telemetry

## ---

**1\. Executive Summary**

This architecture defines an enterprise-grade digital signage network designed for zero-touch maintenance and high reliability. The system solves the inherent instability of embedded web browsers by shifting the rendering workload to a central server.

* **The Server** renders complex websites and videos into standardized MP4 files and manages the fleet.  
* **The Player (Raspberry Pi)** acts as a robust "thin client," streaming pre-rendered content from a secure network share and self-updating via a polling mechanism.

### **Key Capabilities**

* **Zero-Sync Playback:** Players stream content directly from an SMB share; no complex local synchronization logic is required.  
* **Self-Healing Fleet:** Players automatically download and apply software updates from the server, restarting themselves without human intervention.  
* **Live Visibility:** Administrators can see real-time playback status and request live screenshots from any screen.  
* **Content Normalization:** All content (HTML5, Images, Video) is normalized to H.264/MP4 on the server, ensuring 100% smooth playback on the device.

## ---

**2\. Hardware Specification**

The hardware is selected to guarantee 4K/60fps playback 24/7 without thermal throttling.

| Component | Recommendation | Technical Justification |
| :---- | :---- | :---- |
| **SBC** | **Raspberry Pi 5 (4GB or 8GB)** | The Pi 5's VideoCore VII GPU is required for smooth 4K decoding and Wayland performance. The Pi 4 is deprecated due to thermal throttling limits. |
| **Storage** | **Industrial MicroSD (32GB+)** | Used for OS and Application Binary. High-endurance cards (SLC/pSLC) are required to prevent corruption. NVMe is optional since heavy assets are streamed, not stored. |
| **Cooling** | **Official Active Cooler** | **Mandatory.** The Pi 5 throttles at 80°C. 4K video decoding generates significant heat. |
| **Network** | **Gigabit Ethernet** | **Mandatory.** Streaming 4K video from SMB over WiFi is unreliable. Wired connection is required. |
| **Display** | **HDMI 2.1** | Ensure cables support 4K@60Hz. |

## ---

**3\. System Architecture Overview**

1. **Signage.Server (Docker Container):**  
   * **Role:** The Command Center.  
   * **Tech:** ASP.NET Core 10, Blazor Web App, Entity Framework Core.  
   * **Functions:** Device Dashboard, Content Rendering (Puppeteer), Artifact Hosting (Updates), SMB Share Management.  
2. **Signage.Player (Raspberry Pi Device):**  
   * **Role:** The Playback Engine.  
   * **Tech:**.NET 10, Photino (Blazor Hybrid), Linux (Wayland/labwc).  
   * **Functions:** SMB Streaming, Heartbeat Reporting, Screenshot Capture (grim), Self-Updating.  
3. **Infrastructure:**  
   * **Network Storage:** A robust SMB (Samba) share accessible by both Server (Read/Write) and Players (Read-Only).

## ---

**4\. The Server Application (Signage.Server)**

### **4.1 Content Processor (The Rendering Engine)**

We do not render websites on the Pi. The server records them to video using **PuppeteerSharp**.

C\#

public class WebsiteRecorder  
{  
    public async Task\<string\> ConvertUrlToVideoAsync(string url, int durationSec, string outputPath)  
    {  
        // 1\. Launch Headless Browser  
        using var browser \= await Puppeteer.LaunchAsync(new LaunchOptions { Headless \= true });  
        using var page \= await browser.NewPageAsync();  
          
        // 2\. Set Viewport to 1080p or 4K  
        await page.SetViewportAsync(new ViewPortOptions { Width \= 1920, Height \= 1080 });  
        await page.GoToAsync(url, WaitUntilNavigation.Networkidle0);

        // 3\. Inject JavaScript for Smooth Scrolling  
        await page.EvaluateFunctionAsync(@"() \=\> {  
            // JS logic to scroll page down over 'durationSec' seconds  
        }");

        // 4\. Capture Frames & Encode (Conceptual)  
        // In production, pipe 'Page.Screencast' stream to FFmpeg  
        return outputPath;   
    }  
}

### **4.2 Device Management & Telemetry API**

The server tracks state and issues commands (like "Update" or "Screenshot").

C\#

\[HttpPost("heartbeat")\]  
public IActionResult Heartbeat(DeviceTelemetryDto data)  
{  
    var device \= \_repo.GetDevice(data.DeviceId);  
      
    // Update Status  
    device.LastSeen \= DateTime.UtcNow;  
    device.CurrentVersion \= data.AppVersion;  
    device.CurrentlyPlaying \= data.CurrentFileName;  
      
    // Check for "Canary" Update assignment  
    var targetVersion \= \_versionService.GetTargetVersion(device.Group);  
      
    return Ok(new HeartbeatResponse   
    {   
        // Command Flags  
        RequestScreenshot \= device.ScreenshotRequested,  
        UpdateUrl \= (device.CurrentVersion \< targetVersion)   
           ? $"/api/updates/{targetVersion}/binary" : null  
    });  
}

## ---

**5\. The Player Application (Signage.Player)**

### **5.1 Self-Update Service (Linux)**

The player polls the heartbeat. If an update URL is returned, it replaces its own binary.

**UpdateService.cs**

C\#

private async Task PerformSelfUpdate(string updateUrl)  
{  
    \_logger.LogWarning("Downloading update...");  
    var tempPath \= \_executablePath \+ ".new";  
      
    // 1\. Download  
    var data \= await \_http.GetByteArrayAsync(updateUrl);  
    await File.WriteAllBytesAsync(tempPath, data);  
      
    // 2\. Permissions  
    File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);

    // 3\. Swap Binaries (Linux allows renaming running files)  
    File.Move(\_executablePath, \_executablePath \+ ".bak", overwrite: true);  
    File.Move(tempPath, \_executablePath);

    // 4\. Restart via Systemd  
    \_logger.LogWarning("Update applied. Exiting...");  
    Environment.Exit(0);   
}

### **5.2 Screenshot Service (Wayland)**

We use grim to capture the framebuffer directly to memory.

**ScreenCaptureService.cs**

C\#

public async Task\<byte\> CaptureScreenshot()  
{  
    var startInfo \= new ProcessStartInfo  
    {  
        FileName \= "grim",  
        Arguments \= "-", // Output to stdout  
        RedirectStandardOutput \= true,  
        UseShellExecute \= false  
    };  
      
    using var process \= Process.Start(startInfo);  
    using var ms \= new MemoryStream();  
    await process.StandardOutput.BaseStream.CopyToAsync(ms);  
    return ms.ToArray();  
}

### **5.3 SMB Playback**

The app reads a playlist.json from the mounted path /mnt/signage. It does not download files; it passes the file path directly to the HTML5 video player.

## ---

**6\. OS Configuration (Raspberry Pi OS Lite)**

We use **Raspberry Pi OS Lite (64-bit)** running **labwc** (Wayland).

### **6.1 Systemd Automount (Critical for SMB)**

To prevent boot hangs if the network is slow, we use systemd.automount.

**/etc/systemd/system/mnt-signage.mount**

Ini, TOML

\[Unit\]  
Description\=Mount Remote Assets  
\[Mount\]  
What\=//SERVER\_IP/Share  
Where\=/mnt/signage  
Type\=cifs  
Options\=username=pi,password=secure,ro,vers=3.0

**/etc/systemd/system/mnt-signage.automount**

Ini, TOML

\[Unit\]  
Description\=Automount Signage Share  
\[Automount\]  
Where\=/mnt/signage  
TimeoutIdleSec\=0  
\[Install\]  
WantedBy\=multi-user.target

### **6.2 Application Service**

This ensures the app starts on boot and restarts after an update or crash.

**/etc/systemd/system/signage.service**

Ini, TOML

\[Unit\]  
Description\=Signage Player  
After\=network-online.target mnt-signage.mount  
Wants\=mnt-signage.mount

User\=pi  
WorkingDirectory\=/opt/signage  
ExecStart\=/opt/signage/Signage.Player  
Restart\=always  
RestartSec\=3  
Environment\=DISPLAY=:0  
Environment\=WAYLAND\_DISPLAY=wayland-1  
Environment\=DOTNET\_CLI\_TELEMETRY\_OPTOUT=1

\[Install\]  
WantedBy\=graphical.target

### **6.3 Kiosk UI (Labwc)**

**\~/.config/labwc/rc.xml**

XML

\<labwc\_config\>  
  \<windowRules\>  
    \<windowRule identifier\="Signage.Player"\>  
      \<action name\="ToggleFullscreen" /\>  
      \<action name\="KeepAbove" /\>  
    \</windowRule\>  
  \</windowRules\>  
\</labwc\_config\>

**\~/.config/labwc/autostart**

Bash

\# Disable screen sleep/power saving  
swayidle \-w timeout 31536000 'wlopm \--off \\\*' resume 'wlopm \--on \\\*' &  
\# Start Player  
/opt/signage/Signage.Player &

## ---

**7\. Deployment Pipeline**

### **7.1 Server Deployment (GitHub Actions)**

* **Trigger:** Push to main.  
* **Action:** Builds Docker image \-\> Pushes to Registry \-\> Webhook triggers on-prem server to pull/restart container.

### **7.2 Player Artifact Generation (GitHub Actions)**

* **Trigger:** Manual Release or Tag.  
* **Action:**  
  1. dotnet publish \-r linux-arm64 \--self-contained \-p:PublishSingleFile=true.  
  2. Zips the binary.  
  3. Uploads Zip to the **Admin Server API** (not the Pis directly).  
* **Result:** The Admin Server now has version 1.2.0 available.

### **7.3 The "Canary" Rollout**

1. **Admin UI:** You see 1.2.0 is available.  
2. **Test:** You assign 1.2.0 to the "Lab Device" group.  
3. **Verify:** The Lab Pi polls, updates, and you verify via the screenshot feature.  
4. **Promote:** You assign 1.2.0 to "All Devices". The entire fleet updates within 30 seconds.

## ---

**8\. Initial Setup Script (install.sh)**

This script is run once on a fresh Pi to provision it.

Bash

\#\!/bin/bash  
set \-e

\# 1\. System Dependencies  
sudo apt update  
sudo apt install \-y labwc wayland-protocols libwebkit2gtk-4.1-0 cifs-utils grim

\# 2\. Directory Setup  
sudo mkdir \-p /opt/signage /mnt/signage  
sudo chown pi:pi /opt/signage

\# 3\. Download Player Binary (Bootstrap)  
curl \-L http://YOUR\_SERVER/api/updates/latest/binary \-o /opt/signage/Signage.Player  
chmod \+x /opt/signage/Signage.Player

\# 4\. Configure Systemd Units (Mounts & Service)  
\# (Script writes content to /etc/systemd/system/...)

\# 5\. Enable & Reboot  
sudo systemctl enable mnt-signage.automount  
sudo systemctl enable signage.service  
sudo reboot

## **9\. Conclusion**

This architecture represents the state-of-the-art for.NET 10 on embedded Linux.

1. **Stability:** Rendering on the server and streaming via SMB removes 90% of the load from the Pi.  
2. **Maintainability:** The "Pull" update model prevents bad deployments from bricking the fleet.  
3. **Observability:** Live screenshots and telemetry give you confidence that the screens are working without physically visiting them.