#README for JsonImporter Program.cs 

#Purpose
This program downloads designated json files from the foundry/vtt repo into the project to minimize the need for manual data entry and automate formatting.
During this process the program evaluates the json content to determine if the file meets licensing, publication, and other requirements we specify.
The output files are modified and reformatted to remove redundant/useless info, increase readability, and overall make the files more compatible for the needs of this project.


Needs .NET to run:
https://aka.ms/dotnet-download
https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-8.0.22-windows-x64-installer


!! IMPORTANT !!
Program.cs uses an uncommitted "gitToken.txt" text file in order to call the GitHub API.
You must create a gitToken.txt file that contains a github PAT(Personal Access Token) in this directory for the program to work.
You can obtain a personal access token from GitHub and copy-paste it into gitToken.txt.  

How to create a PAT:
1. In the upper-right corner of any page on GitHub, click your profile picture, then click 'Settings'.
2. In the left sidebar, click '<>Developer settings'.
3. In the left sidebar, under  'Personal access tokens', click 'Tokens (classic)'.
4. Select 'Generate new token', then click 'Generate new token (classic)'.
5. Fill out the note and expiration options.
6. Select the scopes you'd like to grant this token, and make sure to include 'repo' in the selection.
7. Click 'Generate token'.
8. Copy the token and paste it into gitToken.txt (or another file where you can find it again later).

More about Personal Access Tokens:
https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens


#Before Use Checklist
1) Make sure gitToken.txt exists and has a valid personal access token
2) Check that 'processingFunctions' in Program.cs includes a processing function for all assets you plan to import, and make sure it matches to the directory you plan to import from
    -Without a valid processing function a file will still be imported, but with minimal alterations
3) Check that Constants.apiRoot matches the desired repo
4) To narrow scope of imported files, adjust Constants.targetDir to change the highest directory of desired content.  
    -No trailing'/',  and default is value is "packs".
5) Check that Contants.localRoot shows the correct root directory for saved files
6) Check 'whitelist'.  This is the list of files/directories that will be retrieved. Use commenting to ignore/unignore specific files/directories.
    -Be aware that some source directories (like equipment and spells) are extremely large
7) Check 'sourceBooks' to make sure it includes only sources that you want to allow
8) Check IsContentApproved. Default enforces that publication is "ORC" and "remaster"

#How To Run
0) Optional: Edit 'targetDir' and/or 'whitelist' in Constants to match the files/directories you want to import.
1) Open terminal
2) cd to <project dir>/JsonImporter
3) run command "dotnet run"
4) Optional: Verify that imported files have intact and properly formatted fields













