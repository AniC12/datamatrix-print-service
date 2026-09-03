# Code Print Manager - Deployment Guide

## Package Information

**Location:** `publish/CodePrintManager/`  
**Size:** ~169 MB  
**Platform:** Windows x64 (self-contained)  
**Build Date:** 2026-09-03

## What's Included

✅ **Self-contained deployment** - No .NET installation required  
✅ **All dependencies included** - SQLite, EF Core, WPF, etc.  
✅ **Multi-language support** - English, Russian, Armenian  
✅ **Ready to run** - Just copy and execute  

## Deployment Steps

### Option 1: Copy to Another PC

1. **Locate the package:**
   ```
   C:\Users\Vahe\CascadeProjects\datamatrix-print-service\publish\CodePrintManager\
   ```

2. **Copy the entire folder** to the target PC:
   - Via USB drive
   - Via network share
   - Via cloud storage (zip it first for easier transfer)

3. **On the target PC:**
   - Place the folder anywhere (e.g., `C:\Program Files\CodePrintManager`)
   - Run `CodePrintManager.Desktop.exe`
   - No installation or admin rights required (unless you place it in Program Files)

### Option 2: Create a ZIP Archive

```powershell
# From the project root:
cd publish
Compress-Archive -Path CodePrintManager -DestinationPath CodePrintManager-v1.0.zip
```

Then transfer `CodePrintManager-v1.0.zip` to the target PC and extract it.

## First Run

When you run the application for the first time:

1. It will create `codeprintmanager.db` (SQLite database)
2. It will create a `logs/` folder for application logs
3. The main window will open with an empty product tree

## Quick Start Guide

### 1. Add a Printer

- Go to **Printers** tab
- Click **Add Printer**
- Enter:
  - **Name:** e.g., "Production Line 1"
  - **IP Address:** e.g., "192.168.1.100"
  - **Port:** 9100 (default for Savema TTO)
- Click **Save**

### 2. Import Product Codes

- Go to **Products** tab
- Right-click in the tree → **Add Product**
- Right-click the product → **Import Codes**
- Select a CSV file with codes (one code per line)

### 3. Create a Print Job

- Go to **Jobs** tab
- Click **New Job**
- Select:
  - Printer
  - Product
  - Quantity
- Click **Create**

## Testing Without Hardware

To test the application without actual printers:

```cmd
CodePrintManager.Desktop.exe --mock
```

This enables mock printer mode - you can add printers and create jobs without real hardware.

## File Structure

```
CodePrintManager/
├── CodePrintManager.Desktop.exe    ← Main executable
├── CodePrintManager.*.dll          ← Application modules
├── *.dll                           ← .NET runtime & dependencies
├── Localization/                   ← Language files
│   ├── en.json
│   ├── ru.json
│   └── hy.json
├── README.txt                      ← User documentation
├── codeprintmanager.db            ← Created on first run
└── logs/                          ← Created on first run
```

## System Requirements

- **OS:** Windows 10 or Windows 11 (64-bit)
- **RAM:** Minimum 2 GB
- **Disk:** 500 MB free space
- **Network:** Required for printer communication (TCP/IP)
- **.NET Runtime:** NOT required (included in package)

## Data Backup

To backup your data, copy these files:

```
codeprintmanager.db
codeprintmanager.db-shm  (if exists)
codeprintmanager.db-wal  (if exists)
```

To restore, copy them back to the application folder.

## Troubleshooting

### Application won't start

- Ensure Windows 10/11 64-bit
- Try running as Administrator
- Check Windows Event Viewer for errors

### Cannot connect to printer

- Verify IP address and port
- Ensure printer is on and connected to network
- Check firewall settings
- Test with `ping <printer-ip>` from Command Prompt

### Database is locked

- Close all application instances
- Delete `.db-shm` and `.db-wal` files
- Restart the application

### Need detailed logs

- Check the `logs/` folder
- Logs are organized by date
- Latest log file contains most recent activity

## Uninstallation

1. Close the application
2. Delete the `CodePrintManager` folder
3. No registry entries or system files are created

**Note:** Backup the database first if you want to keep your data.

## Security Notes

- The application does not require internet access
- No data is sent to external servers
- All data is stored locally in SQLite database
- No user authentication in Phase 1 (all users have full access)

## Known Limitations (Phase 1)

- No barcode scanner integration
- No E-Mark API integration
- No user authentication/authorization
- No cloud backup
- No aggregation features

These features are planned for future phases.

## Support

For issues or questions:

1. Check the `logs/` folder for error details
2. Review the README.txt file
3. Contact your system administrator

## Version History

**v1.0** (2026-09-03)
- Initial release
- Multi-printer support
- CSV code import
- Product tree organization
- Print job management with progress tracking
- Multi-language UI (EN, RU, HY)
- Automatic connection recovery
