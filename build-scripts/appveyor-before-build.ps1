$ErrorActionPreference = "Stop"

#
# Link the Android SDK directory to one without spaces
#
cmd /c mklink /j $env:ANDROID_HOME "C:\Program Files (x86)\Android\android-sdk"

#
# Install the tools, such as proguard
#
Write-Output "y" | & "$env:ANDROID_HOME\tools\android.bat" update sdk --no-ui --all --filter platform-tools,tools

#
# Install the Android 10 (2.3) platfom
#
Write-Output "y" | & "$env:ANDROID_HOME\tools\android.bat" update sdk --no-ui --all --filter android-10
