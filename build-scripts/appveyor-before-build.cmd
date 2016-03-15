
REM
REM Link the Android SDK directory to one without spaces
REM
mklink /j "%ANDROID_HOME%" "C:\Program Files (x86)\Android\android-sdk"

REM
REM Install the tools, such as proguard
REM
echo y | "%ANDROID_HOME%\tools\android.bat" update sdk --no-ui --all --filter platform-tools,tools

REM
REM Install the Android 10 (2.3) platfom
REM
echo y | "%ANDROID_HOME%\tools\android.bat" update sdk --no-ui --all --filter android-10
