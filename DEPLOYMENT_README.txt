================================================================================
  CODE PRINT MANAGER - DEPLOYMENT PACKAGE
================================================================================

Version: 1.0
Build Date: 2026-09-03
Platform: Windows x64

================================================================================
  SYSTEM REQUIREMENTS
================================================================================

- Windows 10 or Windows 11 (64-bit)
- No additional software required (.NET runtime is included)
- Minimum 2 GB RAM
- 500 MB free disk space
- Network connection (for printer communication via TCP/IP)

================================================================================
  INSTALLATION INSTRUCTIONS
================================================================================

1. Copy the entire "CodePrintManager" folder to the target PC
   (You can place it anywhere, e.g., C:\Program Files\CodePrintManager)

2. No installation needed - this is a portable application

3. Run "CodePrintManager.Desktop.exe" to start the application

================================================================================
  FIRST RUN
================================================================================

On first run, the application will automatically create:

- codeprintmanager.db  (SQLite database for storing products, codes, jobs)
- logs/                (Application logs folder)

These files will be created in the same folder as the executable.

================================================================================
  FEATURES
================================================================================

- Import product codes from CSV files
- Organize products in a hierarchical tree structure
- Manage multiple Savema TTO thermal printers
- Create and monitor print jobs with real-time progress tracking
- Multi-language support (English, Russian, Armenian)
- Automatic connection recovery and error handling

================================================================================
  PRINTER SETUP
================================================================================

To add a Savema printer:

1. Go to the "Printers" tab
2. Click "Add Printer"
3. Enter:
   - Name: A friendly name for the printer
   - IP Address: The printer's network IP address
   - Port: 9100 (default for Savema TTO printers)
4. Click "Save"

The application will automatically attempt to connect to the printer.

================================================================================
  MOCK MODE (FOR TESTING WITHOUT HARDWARE)
================================================================================

To run the application without actual printers:

1. Open Command Prompt in the CodePrintManager folder
2. Run: CodePrintManager.Desktop.exe --mock
3. Add printers as usual - they will simulate printing without hardware

================================================================================
  DATA BACKUP
================================================================================

To backup your data, copy these files:

- codeprintmanager.db
- codeprintmanager.db-shm (if exists)
- codeprintmanager.db-wal (if exists)

To restore, copy these files back to the application folder.

================================================================================
  TROUBLESHOOTING
================================================================================

Problem: Application won't start
Solution: 
  - Ensure you have Windows 10/11 64-bit
  - Check Windows Event Viewer for error details
  - Try running as Administrator

Problem: Cannot connect to printer
Solution:
  - Verify printer IP address and port (default: 9100)
  - Ensure printer is powered on and connected to network
  - Check firewall settings
  - Ping the printer IP from Command Prompt

Problem: Database is locked
Solution:
  - Close all instances of the application
  - Delete codeprintmanager.db-shm and codeprintmanager.db-wal files
  - Restart the application

Problem: Need to see detailed logs
Solution:
  - Check the "logs" folder in the application directory
  - Logs are organized by date
  - Send log files when reporting issues

================================================================================
  UNINSTALLATION
================================================================================

To remove the application:

1. Close the application
2. Delete the CodePrintManager folder
3. No registry entries or system files are created

Note: If you want to keep your data, backup the database files first.

================================================================================
  SUPPORT
================================================================================

For technical support or questions, contact your system administrator.

Application logs are stored in the "logs" folder and can be helpful for
troubleshooting issues.

================================================================================
  FILE STRUCTURE
================================================================================

CodePrintManager/
├── CodePrintManager.Desktop.exe    (Main application)
├── *.dll                           (Application libraries)
├── Localization/                   (Language files)
│   ├── en.json                     (English)
│   ├── ru.json                     (Russian)
│   └── hy.json                     (Armenian)
├── codeprintmanager.db             (Created on first run)
├── logs/                           (Created on first run)
└── README.txt                      (This file)

================================================================================
