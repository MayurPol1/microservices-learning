<#
.SYNOPSIS
    End-to-end check of the running MicroShop system.

.DESCRIPTION
    Run it after `docker compose up --build -d`. It talks only to the API gateway, like a real client:
      1. waits until the gateway can reach Catalog (and Catalog its database);
      2. places one order that should be CONFIRMED and one that should be REJECTED;
      3. polls each order until it leaves "Pending" (eventual consistency in action);
      4. shows the notifications and the trace id to open in the Aspire Dashboard.
    Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/smoke-test.ps1
#>
param(
    [string]$Gateway = 'http://localhost:5000',
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'

$KeyboardId = '11111111-1111-1111-1111-111111111111'
$CableId    = '44444444-4444-4444-4444-444444444444'
$MonitorId  = '33333333-3333-3333-3333-333333333333'

function Invoke-Api([string]$Method, [string]$Path, $Body = $null) {
    $request = @{ Method = $Method; Uri = "$Gateway$Path"; UseBasicParsing = $true }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json'
        $request.Body = $Body | ConvertTo-Json -Depth 5
    }
    $response = Invoke-WebRequest @request
    [pscustomobject]@{
        Status  = [int]$response.StatusCode
        TraceId = "$($response.Headers['X-Trace-Id'])"
        Body    = if ($response.Content) { $response.Content | ConvertFrom-Json } else { $null }
    }
}

function Wait-ForOutcome([string]$OrderId) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $last = ''
    while ($true) {
        $order = (Invoke-Api GET "/api/orders/$OrderId").Body
        if ($order.status -ne $last) {
            Write-Host ("     {0:HH:mm:ss.fff}  status = {1}" -f (Get-Date), $order.status)
            $last = $order.status
        }
        if ($order.status -ne 'Pending' -or (Get-Date) -gt $deadline) { return $order }
        Start-Sleep -Milliseconds 300
    }
}

Write-Host "1. Waiting for the system to be ready (gateway -> catalog -> MySQL) ..."
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ($true) {
    try {
        $products = @((Invoke-Api GET '/api/products').Body)
        if ($products.Count -gt 0) { break }
    } catch { }
    if ((Get-Date) -gt $deadline) {
        throw "No products from $Gateway within $TimeoutSeconds s. Check 'docker compose ps' and 'docker compose logs catalog-api'."
    }
    Start-Sleep -Seconds 2
}
$products | Format-Table name, price, availableStock -AutoSize | Out-String | Write-Host

# Put back what the confirmed order below will take, so the script can run again and again.
Invoke-Api POST "/api/products/$KeyboardId/restock" @{ quantity = 1 } | Out-Null
Invoke-Api POST "/api/products/$CableId/restock" @{ quantity = 2 } | Out-Null

Write-Host "2. Placing an order that should be CONFIRMED (1 keyboard, 2 cables)"
$good = Invoke-Api POST '/api/orders' @{
    customerEmail = 'ada@example.com'
    items = @(
        @{ productId = $KeyboardId; quantity = 1 },
        @{ productId = $CableId; quantity = 2 }
    )
}
Write-Host "   HTTP $($good.Status): order $($good.Body.id) is $($good.Body.status), total $($good.Body.total)"
Write-Host "   X-Trace-Id: $($good.TraceId)"
$goodFinal = Wait-ForOutcome $good.Body.id

Write-Host ""
Write-Host "3. Placing an order that should be REJECTED (10 monitors, only 3 in stock)"
$bad = Invoke-Api POST '/api/orders' @{
    customerEmail = 'bob@example.com'
    items = @(@{ productId = $MonitorId; quantity = 10 })
}
Write-Host "   HTTP $($bad.Status): order $($bad.Body.id) is $($bad.Body.status)"
$badFinal = Wait-ForOutcome $bad.Body.id
Write-Host "   reason: $($badFinal.rejectionReason)"

Write-Host ""
Write-Host "4. Notifications for the confirmed order (fan-out: Notifications heard order.placed too)"
$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Milliseconds 500
    $notes = @((Invoke-Api GET "/api/notifications?orderId=$($good.Body.id)").Body)
} while ($notes.Count -lt 2 -and (Get-Date) -lt $deadline)
$notes | Format-Table sentAtUtc, to, subject -AutoSize | Out-String | Write-Host

$passed = $goodFinal.status -eq 'Confirmed' -and $badFinal.status -eq 'Rejected' -and $notes.Count -ge 2

Write-Host "Follow the confirmed order through every service in the Aspire Dashboard:"
Write-Host "   http://localhost:18888/traces/detail/$($good.TraceId)"
Write-Host "   (or open http://localhost:18888, Traces, and filter by that trace id)"
Write-Host ""
if ($passed) {
    Write-Host "PASS: confirmed + rejected + notifications, all through the gateway." -ForegroundColor Green
    exit 0
}
Write-Host "FAIL: expected Confirmed/Rejected and 2 notifications; got '$($goodFinal.status)', '$($badFinal.status)', $($notes.Count) notification(s)." -ForegroundColor Red
exit 1
