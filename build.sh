#!/bin/sh

export EnableNuGetPackageRestore=true
mono ".nuget/NuGet.exe" install Sake -pre -o packages
mono $(find packages/Sake.*/tools/Sake.exe|sort -r|head -n1) -I build -f Sakefile.shade "$@"
