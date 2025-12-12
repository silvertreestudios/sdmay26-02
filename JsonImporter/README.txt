#makeshift README 
#TODO update

!! IMPORTANT !!
Program.cs references uncommitted "gitToken.txt" file to call GitHub API.
You must add gitToken.txt file to this directory for the program to work.
Obtain personal access token from GitHub and copy-paste it into gitToken.txt.  

#Before Use Checklist
1) Make sure gitToken.txt exists and has valid personal access token
2) Check that 'processingFunctions' includes a processing function for assets you plan to import, and make sure it matches to the directory you plan to import from
3) Check that Constants.apiRoot matches the desired repo
4) To narrow scope of imported files, adjust Constants.targetDir to change the highest directory of desired content.  No trailing'/',  and default is value is "packs/pf2e".
5) Check that Contants.localRoot shows the correct root directory for saved files
6) Check 'whitelist'.  This is the list of files/directories that will be retrieved. Use commenting to ignore/unignore specific files.
7) Check 'sourceBooks' to make sure it includes only desired sources
8) Check IsContentApproved. Default enforces that publication is "ORC" and "remaster"

#How To Run
0) Optional: Edit 'targetDir' and/or 'whitelist' in Constants to match the files/directories you want to import.
1) Open terminal
2) cd to <project dir>/JsonImporter
3) dotnet run














