Remove-Item output -Recurse
Remove-Item build -Recurse
mkdir build
mkdir output

Copy-Item ..\WalletWasabi.Fluent.Desktop\bin\dist\win7-x64 build\WalletWasabi -Recurse
Copy-Item Images build\Images  -Recurse
Copy-Item appxmanifest.xml build\appxmanifest.xml

&"C:\Program Files (x86)\Windows Kits\10\bin\10.0.18362.0\x64\MakeAppx.exe" pack /d build /p output\WalletWasabi-1.0.0.8.msix

&"C:\Program Files (x86)\Windows Kits\10\bin\10.0.18362.0\x64\signtool" sign /fd SHA256 /sha1 E70A5E7F058A0E4FCAAC9CC604C44EC8588D1C59 output\*
