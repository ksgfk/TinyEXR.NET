echo "############### Generate C++ project ###############"
$genProc = Start-Process cmake -ArgumentList "-S","native","-B","native/build","-A","x64","-D","CMAKE_CONFIGURATION_TYPES=`"Release`"" -Wait -PassThru -NoNewWindow
If($genProc.ExitCode -ne 0) {
  echo "BUILD FAILED. CMake cannot generate project"
  return $genProc.ExitCode
}

echo "################# Build Native dll #################"
$compileProc = Start-Process cmake -ArgumentList "--build","native/build","--config","Release" -Wait -PassThru -NoNewWindow
If($compileProc.ExitCode -ne 0) {
  echo "BUILD FAILED. CMake cannot compile project"
  return $compileProc.ExitCode
}

echo "################# Copy to C# side #################"
Copy-Item -Path "native/build/lib/Release/TinyEXR.NET.Native.dll" -Destination "TinyEXR.NET\Assets\runtimes\win-x64\native"
echo "DONE."

echo "################# Build C# project #################"
$buildProc = Start-Process dotnet -ArgumentList "build","TinyEXR.NET","--configuration","Release" -Wait -PassThru -NoNewWindow
If($buildProc.ExitCode -ne 0) {
  echo "BUILD FAILED. Cannot build C# project"
  return $compileProc.ExitCode
}

echo "BUILD SUCCESS."
