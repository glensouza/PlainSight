# Running PlainSight Photino Player Locally on Windows

This guide explains how to run and debug the Photino Player on Windows using Visual Studio 2022/2025 or later.

## Prerequisites

### Required Software
- **Visual Studio 2022** or later (Visual Studio 2025/2026 recommended)
  - Workload: ".NET desktop development"
  - Individual component: ".NET 8.0 Runtime"
- **Git for Windows** (for cloning the repository)
- **.NET 8 SDK** (included with Visual Studio)

### Optional but Recommended
- **Visual Studio Code** (alternative to Visual Studio)
- **Windows Terminal** (better command-line experience)

## Setup Instructions

### 1. Clone the Repository

```powershell
# Open PowerShell or Windows Terminal
cd C:\Projects  # Or your preferred directory
git clone https://github.com/glensouza/PlainSight.git
cd PlainSight
```

### 2. Open in Visual Studio

#### Option A: Using Solution File
1. Open Visual Studio
2. Click "Open a project or solution"
3. Navigate to `C:\Projects\PlainSight\PlainSight.slnx`
4. Click "Open"

#### Option B: Using Folder
1. Open Visual Studio
2. Click "Open a local folder"
3. Navigate to `C:\Projects\PlainSight`
4. Click "Select Folder"

### 3. Set Startup Project

1. In Solution Explorer, right-click on `Signage.Player.Photino`
2. Select "Set as Startup Project"

### 4. Create Test Content Folder

```powershell
# Create a local content directory
mkdir C:\PlainSightContent

# Download or copy some test video files to this directory
# Supported formats: MP4, WebM, MKV, AVI, MOV
```

Example: Place a test video at `C:\PlainSightContent\test-video.mp4`

### 5. Configure Launch Settings

#### Method 1: Edit launchSettings.json

Create or edit `src\Signage.Player.Photino\Properties\launchSettings.json`:

```json
{
  "profiles": {
    "Signage.Player.Photino": {
      "commandName": "Project",
      "environmentVariables": {
        "ServerUrl": "https://localhost:7149",
        "ContentPath": "C:\\PlainSightContent",
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  }
}
```

#### Method 2: Use Debug Properties UI

1. Right-click `Signage.Player.Photino` project in Solution Explorer
2. Select "Properties"
3. Navigate to "Debug" → "General"
4. Click "Open debug launch profiles UI"
5. Add environment variables:
   - `ServerUrl` = `https://localhost:7149`
   - `ContentPath` = `C:\PlainSightContent`

### 6. Optional: Create playlist.json

Create `C:\PlainSightContent\playlist.json`:

```json
{
  "Items": [
    "test-video.mp4",
    "announcement.mp4",
    "welcome.mp4"
  ]
}
```

### 7. Build the Project

#### Using Visual Studio
1. Click "Build" → "Build Solution" (or press `Ctrl+Shift+B`)
2. Wait for the build to complete
3. Check the Output window for any errors

#### Using Command Line
```powershell
cd C:\Projects\PlainSight
dotnet build src\Signage.Player.Photino\Signage.Player.Photino.csproj
```

### 8. Run the Player

#### Using Visual Studio
1. Press `F5` to start debugging
2. Or click the green "Play" button (▶️) in the toolbar
3. The Photino window should open in fullscreen

#### Using Command Line
```powershell
cd C:\Projects\PlainSight
dotnet run --project src\Signage.Player.Photino\Signage.Player.Photino.csproj `
  --ServerUrl="https://localhost:7149" `
  --ContentPath="C:\PlainSightContent"
```

## Debugging Tips

### Enable Debug Overlay
While the player is running:
- Press `Ctrl+D` to toggle the debug status overlay
- Shows current video, playlist position, and any errors

### View Console Output
In Visual Studio:
1. Go to "View" → "Output"
2. Select "Debug" from the dropdown
3. Console.WriteLine statements will appear here

### Breakpoints
1. Set breakpoints in the code by clicking the left margin (or press `F9`)
2. When running in debug mode (`F5`), execution will pause at breakpoints
3. Use `F10` to step over, `F11` to step into, and `F5` to continue

### Common Issues

#### Issue: "index.html not found"
**Solution**: Ensure wwwroot folder is being copied to output directory
- Check that `Signage.Player.Photino.csproj` has:
  ```xml
  <ItemGroup>
    <None Update="wwwroot\**\*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
  ```

#### Issue: "Photino.Native.dll not found"
**Solution**: Restore NuGet packages
```powershell
dotnet restore
```

#### Issue: Videos won't play
**Solution**:
1. Verify video files exist in ContentPath
2. Check console output for errors
3. Verify video format is supported (MP4, WebM, etc.)
4. Try with a known-good MP4 file

#### Issue: Window appears but stays black
**Solution**:
1. Check if playlist.json is valid JSON
2. Verify ContentPath environment variable is correct
3. Look for JavaScript errors in the debug output
4. Press `Ctrl+D` to see status overlay

## Testing Without a Server

The player can run without connecting to the PlainSight server:

1. Videos will still play from the local content directory
2. Heartbeat errors will be logged but won't crash the player
3. Self-update and screenshot features won't work

This is useful for:
- UI development and testing
- Video playback validation
- Layout and styling changes

## Hot Reload for HTML/CSS Changes

To see HTML/CSS changes without restarting:

1. Make changes to `wwwroot\index.html`
2. Save the file
3. Stop the player (`Shift+F5`)
4. Run again (`F5`)

Note: Hot reload doesn't work automatically for HTML files in Photino

## Building for Release

### Create Release Build
```powershell
dotnet publish src\Signage.Player.Photino\Signage.Player.Photino.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -p:PublishSingleFile=true `
  -o .\publish\photino-windows
```

Output: `.\publish\photino-windows\Signage.Player.Photino.exe`

### Test Release Build
```powershell
cd .\publish\photino-windows
$env:ServerUrl="https://localhost:7149"
$env:ContentPath="C:\PlainSightContent"
.\Signage.Player.Photino.exe
```

## Project Structure

```
Signage.Player.Photino/
├── Program.cs              # Application entry point
├── VideoFormats.cs         # Supported video formats
├── Services/
│   ├── HeartbeatService.cs      # Server communication
│   ├── UpdateService.cs         # Self-update logic
│   ├── ScreenCaptureService.cs  # Screenshot capture (Windows: not supported)
│   └── PlaylistService.cs       # Playlist management
└── wwwroot/
    └── index.html          # Video player UI (HTML/CSS/JS)
```

## Performance Tips

### For Development
- Use Debug configuration for detailed logs
- Enable debug overlay (`Ctrl+D`) to monitor status
- Use smaller test videos (< 100MB) for faster iteration

### For Testing
- Use Release configuration for better performance
- Test with production-quality videos (1080p, 4K)
- Test playlist transitions and auto-loop behavior

## Advanced Configuration

### Custom Window Size (Non-Fullscreen)

Edit `Program.cs`:
```csharp
_window = new PhotinoWindow()
    .SetTitle("PlainSight Player")
    .SetSize(new Size(1280, 720))  // Change to desired size
    .SetFullScreen(false)           // Disable fullscreen
    .SetResizable(true)             // Allow window resize
    // ... rest of configuration
```

### Custom Content Path

Set via environment variable or command line:
```powershell
# Environment variable
$env:ContentPath="D:\Videos\Signage"

# Or command line argument
dotnet run --ContentPath="D:\Videos\Signage"
```

## Troubleshooting

### Enable Verbose Logging

Add to `Program.cs` after logger initialization:
```csharp
builder.SetMinimumLevel(LogLevel.Debug);
```

### Check Dependencies

```powershell
dotnet list package
```

Expected packages:
- Photino.NET (4.0.16)
- Microsoft.Extensions.Logging.Console (10.0.2)
- Microsoft.Extensions.Configuration (10.0.2)

### Verify .NET Version

```powershell
dotnet --version
# Should show 8.0.x or later
```

## Getting Help

If you encounter issues:
1. Check the console output for error messages
2. Enable debug overlay (`Ctrl+D`) for runtime status
3. Review the logs in Visual Studio Output window
4. Open an issue on GitHub with:
   - Error messages
   - Steps to reproduce
   - Visual Studio version
   - .NET SDK version
   - Windows version

## Next Steps

After successfully running locally:
1. Review the code in `Program.cs` and `wwwroot/index.html`
2. Make changes and test
3. Submit pull requests for improvements
4. Deploy to Raspberry Pi for testing

## See Also

- [Main README](../../README.md) - Project overview
- [Photino Player README](../Signage.Player.Photino/README.md) - Photino player documentation
- [Architecture Guide](../../docs/architecture.md) - System architecture
- [Raspberry Pi Setup](../../docs/raspberry-pi-setup.md) - Deploy to production
