<#
.SYNOPSIS
    Checks an in-house AI server (Ollama, vLLM, LM Studio) the way LT ODM calls it, before pointing jobs at it.

.DESCRIPTION
    Run it on the web server (so the network path and firewall are tested too). Makes the same calls as the app:
      1. GET  {Endpoint}/models                 the models are there (Settings > AI connections > Test does the same)
      2. POST {Endpoint}/chat/completions       Text job: a JSON answer that follows a schema
      3. POST {Endpoint}/chat/completions       Vision job: reads a picture (data: URL) into JSON
      4. POST {Endpoint}/embeddings             Embedding job: one vector of exactly 1024 numbers (image search needs it)
    Only the test sentence and the picture given (or a drawn test picture) are sent. Nothing leaves the LT network when
    the endpoint is an in-house server. Each step prints PASS / FAIL and how long it took (on a server without a GPU the
    first call to each model also loads it, which can take a minute).

.EXAMPLE
    .\tools\test-ai-server.ps1 -Endpoint http://ai-server.lt.local:11434/v1

.EXAMPLE
    .\tools\test-ai-server.ps1 -Endpoint http://10.1.2.3:11434/v1 -VisionModel gemma3:4b -Picture D:\samples\jacket.jpg
#>
param(
    [Parameter(Mandatory)] [string] $Endpoint,
    [string] $TextModel = 'qwen2.5:7b',
    [string] $VisionModel = 'qwen2.5vl:7b',
    [string] $EmbeddingModel = 'qwen3-embedding:0.6b',
    [string] $ApiKey = '',
    [string] $Picture = '',
    [int] $TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$Endpoint = $Endpoint.TrimEnd('/')
$results = New-Object System.Collections.Generic.List[object]

function Call($method, $path, $body) {
    $headers = @{}
    if ($ApiKey) { $headers['Authorization'] = "Bearer $ApiKey" }
    $params = @{ Method = $method; Uri = "$Endpoint/$path"; Headers = $headers; TimeoutSec = $TimeoutSeconds }
    if ($null -ne $body) {
        $params['ContentType'] = 'application/json; charset=utf-8'
        $params['Body'] = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 30 -Compress))
    }
    Invoke-RestMethod @params
}

function Step($name, [scriptblock] $action) {
    Write-Host "`n== $name" -ForegroundColor Cyan
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        $detail = & $action
        $results.Add([pscustomobject]@{ Step = $name; Result = 'PASS'; Seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1); Detail = $detail })
        Write-Host "PASS ($([math]::Round($clock.Elapsed.TotalSeconds, 1)) s) $detail" -ForegroundColor Green
    }
    catch {
        $results.Add([pscustomobject]@{ Step = $name; Result = 'FAIL'; Seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1); Detail = $_.Exception.Message })
        Write-Host "FAIL $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Answer($response) {
    $text = [string]$response.choices[0].message.content
    $text = [regex]::Replace($text, '(?s)<think>.*?</think>', '').Trim()
    if ($text.StartsWith('```')) { $text = ($text -replace '^```[a-zA-Z]*\s*', '' -replace '```\s*$', '').Trim() }
    $text | ConvertFrom-Json
}

function Schema($properties, $required) {
    @{ type = 'json_schema'; json_schema = @{ name = 'answer'; strict = $false; schema = @{ type = 'object'; properties = $properties; required = $required } } }
}

function TestPicture {
    if ($Picture) {
        $bytes = [IO.File]::ReadAllBytes((Resolve-Path $Picture))
        $type = switch ([IO.Path]::GetExtension($Picture).ToLowerInvariant()) { '.png' { 'image/png' } '.webp' { 'image/webp' } default { 'image/jpeg' } }
        return "data:$type;base64,$([Convert]::ToBase64String($bytes))"
    }
    # A simple navy T-shirt with a white stripe, drawn here so the test needs no file.
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object Drawing.Bitmap 384, 384
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.Clear([Drawing.Color]::White)
    $navy = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(20, 30, 80))
    $points = @(
        (New-Object Drawing.Point 120, 40), (New-Object Drawing.Point 160, 60), (New-Object Drawing.Point 224, 60), (New-Object Drawing.Point 264, 40),
        (New-Object Drawing.Point 350, 110), (New-Object Drawing.Point 310, 160), (New-Object Drawing.Point 280, 140), (New-Object Drawing.Point 280, 350),
        (New-Object Drawing.Point 104, 350), (New-Object Drawing.Point 104, 140), (New-Object Drawing.Point 74, 160), (New-Object Drawing.Point 34, 110))
    $g.FillPolygon($navy, [Drawing.Point[]]$points)
    $g.FillRectangle([Drawing.Brushes]::White, 104, 190, 176, 18)
    $stream = New-Object IO.MemoryStream
    $bmp.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "data:image/png;base64,$([Convert]::ToBase64String($stream.ToArray()))"
}

Write-Host "AI server: $Endpoint"

Step 'Models' {
    $ids = @((Call 'GET' 'models' $null).data | ForEach-Object { $_.id })
    $missing = @($TextModel, $VisionModel, $EmbeddingModel | Where-Object { $ids -notcontains $_ })
    if ($missing.Count) { throw "Not on the server: $($missing -join ', '). Available: $($ids -join ', '). Pull them (ollama pull <name>)." }
    "$($ids.Count) models: $($ids -join ', ')"
}

Step "Text ($TextModel)" {
    $body = @{
        model = $TextModel; temperature = 0.1
        messages = @(
            @{ role = 'system'; content = 'You turn a request into filters for a garment style library. Answer in JSON.' },
            @{ role = 'user'; content = "men's recycled polyester running jackets for SS27" })
        response_format = (Schema @{ gender = @{ type = 'string' }; productType = @{ type = 'string' }; season = @{ type = 'string' } } @('gender', 'productType'))
    }
    $a = Answer (Call 'POST' 'chat/completions' $body)
    if (-not $a.gender) { throw 'The answer did not follow the schema (no gender).' }
    "gender=$($a.gender), productType=$($a.productType), season=$($a.season)"
}

Step "Vision ($VisionModel)" {
    $body = @{
        model = $VisionModel; temperature = 0.1
        messages = @(
            @{ role = 'system'; content = 'You describe one garment picture. Answer in JSON.' },
            @{ role = 'user'; content = @(
                @{ type = 'text'; text = 'Describe this garment picture.' },
                @{ type = 'image_url'; image_url = @{ url = (TestPicture) } }) })
        response_format = (Schema @{ garment = @{ type = 'string' }; colours = @{ type = 'array'; items = @{ type = 'string' } } } @('garment', 'colours'))
    }
    $a = Answer (Call 'POST' 'chat/completions' $body)
    if (-not $a.garment) { throw 'The model did not describe the picture (is it a vision model?).' }
    "garment=$($a.garment); colours=$(@($a.colours) -join ', ')"
}

Step "Embedding ($EmbeddingModel)" {
    $v = (Call 'POST' 'embeddings' @{ model = $EmbeddingModel; input = 'navy hooded track jacket with white side stripes' }).data[0].embedding
    $n = @($v).Count
    if ($n -ne 1024) { throw "The model returns $n numbers; image search needs 1024 (e.g. qwen3-embedding:0.6b or bge-m3)." }
    '1024 numbers per text'
}

Write-Host ''
$results | Format-Table Step, Result, Seconds, Detail -AutoSize -Wrap
if ($results | Where-Object Result -eq 'FAIL') { exit 1 }
