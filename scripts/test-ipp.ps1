[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Server,
    [Parameter(Mandatory)]
    [string]$PrinterSlug,
    [ValidateRange(1, 65535)]
    [int]$Port = 631
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-UInt16BE {
    param([IO.BinaryWriter]$Writer, [int]$Value)
    $Writer.Write([byte](($Value -shr 8) -band 0xFF))
    $Writer.Write([byte]($Value -band 0xFF))
}

function Write-Int32BE {
    param([IO.BinaryWriter]$Writer, [int]$Value)
    $Writer.Write([byte](($Value -shr 24) -band 0xFF))
    $Writer.Write([byte](($Value -shr 16) -band 0xFF))
    $Writer.Write([byte](($Value -shr 8) -band 0xFF))
    $Writer.Write([byte]($Value -band 0xFF))
}

function Write-IppString {
    param(
        [IO.BinaryWriter]$Writer,
        [byte]$Tag,
        [string]$Name,
        [string]$Value
    )
    $NameBytes = [Text.Encoding]::UTF8.GetBytes($Name)
    $ValueBytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $Writer.Write($Tag)
    Write-UInt16BE $Writer $NameBytes.Length
    $Writer.Write($NameBytes)
    Write-UInt16BE $Writer $ValueBytes.Length
    $Writer.Write($ValueBytes)
}

$Uri = "http://$Server`:$Port/ipp/printers/$PrinterSlug"
$Memory = [IO.MemoryStream]::new()
$Writer = [IO.BinaryWriter]::new($Memory)
try {
    $Writer.Write([byte]2)
    $Writer.Write([byte]0)
    Write-UInt16BE $Writer 0x000B
    Write-Int32BE $Writer 1
    $Writer.Write([byte]0x01)
    Write-IppString $Writer 0x47 'attributes-charset' 'utf-8'
    Write-IppString $Writer 0x48 'attributes-natural-language' 'es'
    Write-IppString $Writer 0x45 'printer-uri' $Uri.Replace('http://', 'ipp://')
    Write-IppString $Writer 0x44 'requested-attributes' 'all'
    $Writer.Write([byte]0x03)
    $Writer.Flush()
    $RequestBytes = $Memory.ToArray()
}
finally {
    $Writer.Dispose()
    $Memory.Dispose()
}

$Client = [Net.Http.HttpClient]::new()
try {
    $Content = [Net.Http.ByteArrayContent]::new($RequestBytes)
    $Content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/ipp')
    $Response = $Client.PostAsync($Uri, $Content).GetAwaiter().GetResult()
    $ResponseBytes = $Response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    if (-not $Response.IsSuccessStatusCode) {
        throw "HTTP status $([int]$Response.StatusCode)"
    }
    if ($ResponseBytes.Length -lt 9) {
        throw 'The response is not a complete IPP message.'
    }

    $IppStatus = (($ResponseBytes[2] -shl 8) -bor $ResponseBytes[3])
    if ($IppStatus -ne 0) {
        throw ('IPP returned status 0x{0:X4}' -f $IppStatus)
    }

    Write-Host "MiniPrint answered correctly at $Uri" -ForegroundColor Green
    Write-Host "IPP status: 0x$($IppStatus.ToString('X4')); response bytes: $($ResponseBytes.Length)"
}
finally {
    $Client.Dispose()
}
