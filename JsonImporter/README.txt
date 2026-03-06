#makeshift README 
#TODO update

Needs .NET to run:
https://aka.ms/dotnet-download
https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-8.0.22-windows-x64-installer

!! IMPORTANT !!
Program.cs references uncommitted "gitToken.txt" file to call GitHub API.
You must add gitToken.txt file to this directory for the program to work.
Obtain personal access token from GitHub and copy-paste it into gitToken.txt.  

#Before Use Checklist
1) Make sure gitToken.txt exists and has valid personal access token
2) Check that 'processingFunctions' in Program.cs includes a processing function for all assets you plan to import, and make sure it matches to the directory you plan to import from
    -Without a valid processing function a file will still be imported, but with minimal alterations
3) Check that Constants.apiRoot matches the desired repo
4) To narrow scope of imported files, adjust Constants.targetDir to change the highest directory of desired content.  
    -No trailing'/',  and default is value is "packs".
5) Check that Contants.localRoot shows the correct root directory for saved files
6) Check 'whitelist'.  This is the list of files/directories that will be retrieved. Use commenting to ignore/unignore specific files/directories.
    -Be aware that some source directories (like equipment and spells) are extremely large
7) Check 'sourceBooks' to make sure it includes only desired sources
8) Check IsContentApproved. Default enforces that publication is "ORC" and "remaster"

#How To Run
0) Optional: Edit 'targetDir' and/or 'whitelist' in Constants to match the files/directories you want to import.
1) Open terminal
2) cd to <project dir>/JsonImporter
3) dotnet run
4) Optional: Verify that imported files have intact and properly formatted fields













