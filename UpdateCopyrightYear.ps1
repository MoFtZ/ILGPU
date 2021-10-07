## ---------------------------------------------------------------------------------------
##                                        ILGPU
##                           Copyright (c) 2021 ILGPU Project
##                                    www.ilgpu.net
##
## File: UpdateCopyrightYear.ps1
##
## This file is part of ILGPU and is distributed under the University of Illinois Open
## Source License. See LICENSE.txt for details.
## ---------------------------------------------------------------------------------------

using namespace System
using namespace System.IO
using namespace System.Text
using namespace System.Text.RegularExpressions

##
## CopyrightInfo
##
## Helper class to hold copyright information.
##
class CopyrightInfo {
    ##
    ## Properties
    ##

    ## The contents of the file before the copyright.
    [string] $Prefix

    ## The contents of the copyright.
    [string] $Copyright

    ## The contents of the file after the copyright.
    [string] $Suffix

    ## The starting year of the copyright.
    [Nullable[int]] $StartingYear

    ##
    ## Methods
    ##

    ## Constructor
    CopyrightInfo([string] $fileContents) {
        $this.Prefix = [string]::Empty
        $this.Copyright = [string]::Empty
        $this.Suffix = $fileContents
        $this.StartingYear = $null
    }

    ## Constructor
    CopyrightInfo([string] $prefix, [string] $copyright, [string] $suffix, [Nullable[int]] $startingYear) {
        $this.Prefix = $prefix
        $this.Copyright = $copyright
        $this.Suffix = $suffix
        $this.StartingYear = $startingYear
    }
}

##
## BaseCopyrightProcessor
##
## Abstract base class for processing copyright information.
##
class BaseCopyrightProcessor {
    ##
    ## Static
    ##
    static [int] $LineLength = 90

    static [string] $CopyrightText = 'Copyright (c)'

    static [string] $CopyrightOwner = 'ILGPU Project'

    static [string] $CopyrightOwnerWebsite = 'www.ilgpu.net'

    static [string[]] $CopyrightLicenseText = (
        "This file is part of ILGPU and is distributed under the University of Illinois Open",
        "Source License. See LICENSE.txt for details."
    )

    ## Returns the current/ending copyright year.
    static [int] GetCopyrightYear() {
        return (Get-Date).Year
    }

    ## Helper function to align text in the middle of a line.
    static [string] CenterAlignString([string] $text, [int] $lineLength) {
        $paddingLength = $lineLength - $text.Length
        $lefPad = ' ' * ($paddingLength / 2)
        return $lefPad + $text
    }

    ## Helper function for parsing the contents of the file using
    ## a regular expression with the named groups:
    ## - prefix
    ## - copyright
    ## - suffix
    ## - startingYear
    static hidden [CopyrightInfo] ParseUsingRegex([FileInfo] $file, [string] $regexPattern) {
        # Read existing file contents.
        $filePath = $file.FullName
        $fileContents = [File]::ReadAllText($filePath)

        $matches = [Regex]::Match($fileContents, $regexPattern, [RegexOptions]::SingleLine)
        if ($matches.Success) {
            $prefix = $matches.Groups['prefix'].Value
            $copyright = $matches.Groups['copyright'].Value
            $suffix = $matches.Groups['suffix'].Value
            $startingYear = [Nullable[int]]$matches.Groups['startingYear'].Value

            return [CopyrightInfo]::new($prefix, $copyright, $suffix, $startingYear)
        } else {
            return [CopyrightInfo]::new($fileContents)
        }
    }

    ## Write the updated copyright information to the file.
    static hidden [void] WriteCopyrightToFile([FileInfo] $file, [CopyrightInfo] $copyrightInfo) {
        $encoding = [Encoding]::UTF8
        $fileContents = $copyrightInfo.Prefix + $copyrightInfo.Copyright + $copyrightInfo.Suffix
        [File]::WriteAllText($file.FullName, $fileContents, $encoding)
    }

    ##
    ## Methods
    ##

    ## Constructor
    BaseCopyrightProcessor() {
        $type = $this.GetType()
        if ($type -eq [BaseCopyrightProcessor]) {
            throw("Class $type must be inherited")
        }
    }

    ## Returns true if the file can be parsed by this processor.
    [bool] CanParse([FileInfo] $file) {
        throw("Must override method")
    }

    ## Parses the file and extracts the copyright information.
    hidden [CopyrightInfo] Parse([FileInfo] $file) {
        throw("Must override method")
    }

    ## Builds the copyright string, with an optional starting year.
    hidden [string] BuildCopyright([FileInfo] $file, [string] $copyrightYear, [bool] $hasExistingCopyright) {
        throw("Must override method")
    }

    ## Parses the file and updates the existing copyright, or
    ## adds copyright information to the file.
    [void] AddOrUpdateCopyright([FileInfo] $file) {
        if ($this.CanParse($file)) {
            # Parse the copyright information from the file.
            $copyrightInfo = $this.Parse($file)

            # Build the copyright year range.
            $hasExistingCopyright = -not ($copyrightInfo.StartingYear -eq $null)
            $endingYear = $this::GetCopyrightYear()

            if ($hasExistingCopyright -and $copyrightInfo.StartingYear -ne $endingYear) {
                $copyrightYear = "$($copyrightInfo.StartingYear)-${endingYear}"
            } else {
                $copyrightYear = "${endingYear}"
            }

            # Write the updated copyright and file contents.
            $copyrightInfo.Copyright = $this.BuildCopyright($file, $copyrightYear, $hasExistingCopyright)
            $this::WriteCopyrightToFile($file, $copyrightInfo)
        }
    }
}

##
## SourceCodeCopyrightProcessor
##
## Updates copyright for source code files.
##
class SourceCodeCopyrightProcessor : BaseCopyrightProcessor {
    ##
    ## Static
    ##

    ## The delimeter used for padding the line.
    static [string] $LineDelimiter = '-'

    ## Returns the comment prefix for the given file.
    static hidden [string] GetLinePrefix([FileInfo] $file) {
        if ($file.Extension -eq '.ps1') {
            return '## '
        } else {
            return '// '
        }
    }

    ## Returns the filename line of the copyright header.
    static hidden [string] MakeFilenameLine([FileInfo] $file)  {
        if ($file.Extension -eq ".tt") {
            $fileName = "$($file.Name)/$($file.BaseName).cs"
        } else {
            $fileName = $file.Name
        }
        return "File: $fileName"
    }

    # Returns the name of the project to which this file belongs.
    static hidden [string] MakeProjectLine([FileInfo] $file) {
        $samplesRoot = [Path]::Combine($PSScriptRoot, 'Samples')
        $algorithmsRoot = [Path]::Combine($PSScriptRoot, 'Src', 'ILGPU.Algorithms')

        if ($file.FullName.StartsWith($samplesRoot)) {
            return 'ILGPU Samples'
        } elseif ($file.FullName.StartsWith($algorithmsRoot)) {
            return 'ILGPU Algorithms'
        }

        return 'ILGPU'
    }

    ##
    ## Methods
    ##

    ## Implements BaseCopyrightProcessor.CanParse
    [bool] CanParse([FileInfo] $file) {
        # Ignore unsupported file extensions.
        $supportedExtensions = ('.cs', '.fs', '.tt', '.ttinclude', '.ps1')
        if ($supportedExtensions -notcontains $file.Extension) {
            return $false
        }

        # Ignore files in the Resources directory.
        if ($file.Directory.Name -Eq "Resources") {
            return $false
        }

        # Ignore files in the obj directory.
        if ($file.FullName -like '*\obj\*' -or $file.FullName -like '*\obj\*') {
            return $false
        }

        # Ignore generated C# files.
        if ($file.Extension -eq ".cs") {
            $templatePath = [Path]::ChangeExtension($file.FullName, ".tt")
            if (Test-Path -Path $templatePath -PathType Leaf) {
                return $false
            }
        }

        # Ignore AssemblyAttributes.cs file.
        if ($file.Name -eq 'AssemblyAttributes.cs') {
            return $false
        }

        return $true
    }

    ## Implements BaseCopyrightProcessor.Parse
    [CopyrightInfo] Parse([FileInfo] $file) {
        # Search for the copyright block between two lines of "// ----------".
        $linePrefix = $this::GetLinePrefix($file)
        $prefix = [Regex]::Escape($linePrefix + ($this::LineDelimiter * 10))
        $delim = [Regex]::Escape($this::LineDelimiter)
        $copyrightText = [Regex]::Escape($this::CopyrightText)

        $pattern = "^(?<prefix>.*)(?<copyright>${prefix}.*?${copyrightText}\s*(?<startingYear>[\d]*).*?${prefix}[${delim}]+)(?<suffix>.*)"
        return $this::ParseUsingRegex($file, $pattern)
    }

    ## Implements BaseCopyrightProcessor.BuildCopyright
    hidden [string] BuildCopyright([FileInfo] $file, [string] $copyrightYear, [bool] $hasExistingCopyright) {
        $linePrefix = $this::GetLinePrefix($file)
        $lineLength = $this::LineLength - $linePrefix.Length
        $alignmentLength = $this::LineLength - ($linePrefix.Length * 2) - 1
        $dashLine = $this::LineDelimiter * $lineLength

        $projectLine = $this::MakeProjectLine($file)
        $copyrightLine = "$($this::CopyrightText) ${copyrightYear} $($this::CopyrightOwner)"
        $websiteLine = $this::CopyrightOwnerWebsite
        $fileLine = $this::MakeFilenameLine($file)

        $lines = (
            $dashLine,
            ($this::CenterAlignString($projectLine, $alignmentLength)),
            ($this::CenterAlignString($copyrightLine, $alignmentLength)),
            ($this::CenterAlignString($websiteLine, $alignmentLength)),
            [string]::Empty,
            $fileLine,
            [string]::Empty,
            $this::CopyrightLicenseText,
            $dashLine
        )

        # Append the comment prefix to each line.
        $lines = $lines | % { $_ } | % { "${linePrefix}$_".TrimEnd() }
        $copyrightHeader = [string]::Join([Environment]::NewLine, $lines)

        if (-not $hasExistingCopyright) {
            $copyrightHeader += [Environment]::NewLine + [Environment]::NewLine
        }
        return $copyrightHeader
    }
}

##
## NuspecCopyrightProcessor
##
## Updates copyright for Nuspec targets.
##
class NuspecCopyrightProcessor : BaseCopyrightProcessor {
    ##
    ## Methods
    ##

    ## Implements BaseCopyrightProcessor.CanParse
    [bool] CanParse([FileInfo] $file) {
        return $file.Name.EndsWith(".nuspec.targets")
    }

    ## Implements BaseCopyrightProcessor.Parse
    [CopyrightInfo] Parse([FileInfo] $file) {
        # Search for the copyright text between <Copyright> and </Copyright> tags.
        $copyrightText = [Regex]::Escape($this::CopyrightText)
        $pattern = "^(?<prefix>.*\<Copyright\>)(?<copyright>${copyrightText}\s*(?<startingYear>[\d]*).+)(?<suffix>\</Copyright\>.*)"
        return $this::ParseUsingRegex($file, $pattern)
    }

    ## Implements BaseCopyrightProcessor.BuildCopyright
    hidden [string] BuildCopyright([FileInfo] $file, [string] $copyrightYear, [bool] $hasExistingCopyright) {
        return "$($this::CopyrightText) ${copyrightYear} $($this::CopyrightOwner). All rights reserved."
    }
}

##
## LicenseCopyrightProcessor
##
## Updates copyright for LICENSE.txt.
##
class LicenseCopyrightProcessor : BaseCopyrightProcessor {
    ##
    ## Methods
    ##

    ## Implements BaseCopyrightProcessor.CanParse
    [bool] CanParse([FileInfo] $file) {
        return $file.Name -eq 'LICENSE.txt'
    }

    ## Implements BaseCopyrightProcessor.Parse
    [CopyrightInfo] Parse([FileInfo] $file) {
        # Search for the copyright line.
        $copyrightText = [Regex]::Escape($this::CopyrightText)
        $pattern = "^(?<prefix>.*)(?<copyright>${copyrightText}\s*(?<startingYear>[\d]*).+)(?<suffix>\s+All rights reserved.*)"
        return $this::ParseUsingRegex($file, $pattern)
    }

    ## Implements BaseCopyrightProcessor.BuildCopyright
    hidden [string] BuildCopyright([FileInfo] $file, [string] $copyrightYear, [bool] $hasExistingCopyright) {
        return "$($this::CopyrightText) ${copyrightYear} $($this::CopyrightOwner)"
    }
}

# Recursively iterate over the list of folders and find all files that
# match the processors. Updates the copyright header if the file has an
# existing copyright. Otherwise, add a new copyright header.
Function Add-Or-Update-Copyright-On-Folders([String[]] $folders, [BaseCopyrightProcessor[]] $processors) {
    Foreach ($folder in $folders) {
        Foreach ($file in Get-ChildItem -Path $folder -Recurse -File) {
            $processor = $processors | Where { $_.CanParse($file) } | Select -First 1
            if ($processor) {
                $processor.AddOrUpdateCopyright($file)
            }
        }
    }
}

# Main entry point
$ConfigMap = @{
    Folders = (
        $PSScriptRoot
    )
    Processors = (
        [SourceCodeCopyrightProcessor]::new(),
        [NuspecCopyrightProcessor]::new(),
        [LicenseCopyrightProcessor]::new()
    )
}

Add-Or-Update-Copyright-On-Folders $ConfigMap.Folders $ConfigMap.Processors
