$genProc = Start-Process cmake -ArgumentList "-S","native","-B","native/build","-A","x64","-D","CMAKE_CONFIGURATION_TYPES=`"Release`"" -Wait -PassThru -NoNewWindow
If($genProc.ExitCode -ne 0) {
  echo "CMake cannot generate project"
  return $genProc.ExitCode
}

$compileProc = Start-Process cmake -ArgumentList "--build","native/build","--config","Release" -Wait -PassThru -NoNewWindow
If($compileProc.ExitCode -ne 0) {
  echo "CMake cannot compile project"
  return $compileProc.ExitCode
}

Copy-Item -Path "native/build/lib/Release/TinyEXR.NET.Native.dll" -Destination "TinyEXR.NET\Assets\runtimes\win-x64"