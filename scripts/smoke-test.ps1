<#
.SYNOPSIS
    End-to-end check of the running MicroShop system.

.DESCRIPTION
    Run it after `docker compose up --build -d`. It talks only to the API gateway, like a real client:
      1. waits until the gateway can reach Catalog (and Catalog its database);
      2. places one order that should be CONFIRMED and one that should be REJECTED;
      3. polls each order until it leaves "Pending" (eventual consistency in action);
      4. shows the notifications and the trace id to open in the Aspire Dashboard;
      5. asks Payments for a Stripe Checkout page for the confirmed order (skipped without a Stripe test key);
      6. cancels the confirmed order and checks that Catalog gives the stock back (and Payments closes the page);
      7. checks the roles: anonymous, customer and admin get 401, 403 and 200 where they should.
    Paying itself needs a browser on Stripe's page, so the script can't do it: try it in the web app.
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

# Demo accounts seeded by the Identity service (see README).
$AdminLogin    = @{ email = 'admin@microshop.local';    password = 'Admin123!' }
$CustomerLogin = @{ email = 'customer@microshop.local'; password = 'Customer123!' }

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null) {
    $request = @{ Method = $Method; Uri = "$Gateway$Path"; UseBasicParsing = $true }
    if ($Token) {
        $request.Headers = @{ Authorization = "Bearer $Token" }
    }
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

# Like Invoke-Api, but returns only the HTTP status, also for 4xx answers (which Invoke-WebRequest throws).
function Get-StatusCode([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null) {
    try { (Invoke-Api $Method $Path $Body $Token).Status }
    catch { [int]$_.Exception.Response.StatusCode }
}

function Wait-ForOutcome([string]$OrderId) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $last = ''
    while ($true) {
        $order = (Invoke-Api GET "/api/orders/$OrderId" -Token $customerToken).Body
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

# Log in through the gateway (Identity issues the JWTs).
$adminToken = (Invoke-Api POST '/api/auth/login' $AdminLogin).Body.accessToken
$customerToken = (Invoke-Api POST '/api/auth/login' $CustomerLogin).Body.accessToken
Write-Host "   Logged in as $($AdminLogin.email) (Admin) and $($CustomerLogin.email) (Customer)"

# Step 6 cancels the confirmed order, which gives its stock back, so the script can run again and again.

Write-Host "2. Placing an order that should be CONFIRMED (1 keyboard, 2 cables)"
# No email in the body: the order's owner comes from the customer's token.
$good = Invoke-Api POST '/api/orders' @{
    items = @(
        @{ productId = $KeyboardId; quantity = 1 },
        @{ productId = $CableId; quantity = 2 }
    )
} $customerToken
Write-Host "   HTTP $($good.Status): order $($good.Body.id) is $($good.Body.status), total $($good.Body.total)"
Write-Host "   X-Trace-Id: $($good.TraceId)"
$goodFinal = Wait-ForOutcome $good.Body.id

Write-Host ""
Write-Host "3. Placing an order that should be REJECTED (10 monitors, only 3 in stock)"
$bad = Invoke-Api POST '/api/orders' @{
    items = @(@{ productId = $MonitorId; quantity = 10 })
} $customerToken
Write-Host "   HTTP $($bad.Status): order $($bad.Body.id) is $($bad.Body.status)"
$badFinal = Wait-ForOutcome $bad.Body.id
Write-Host "   reason: $($badFinal.rejectionReason)"

Write-Host ""
Write-Host "4. Notifications for the confirmed order (fan-out: Notifications heard order.placed too)"
$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Milliseconds 500
    $notes = @((Invoke-Api GET "/api/notifications?orderId=$($good.Body.id)" -Token $customerToken).Body)
} while ($notes.Count -lt 2 -and (Get-Date) -lt $deadline)
$notes | Format-Table sentAtUtc, to, subject -AutoSize | Out-String | Write-Host

$confirmedOk = $goodFinal.status -eq 'Confirmed' -and $badFinal.status -eq 'Rejected' -and $notes.Count -ge 2

Write-Host "5. Asking Payments for a Stripe Checkout page for the confirmed order"
$stripeConfigured = $false
try {
    $checkout = (Invoke-Api POST '/api/payments/checkout' @{ orderId = $good.Body.id } $customerToken).Body
    $stripeConfigured = $true
    Write-Host "   Stripe test page ready (payable until $($checkout.expiresAtUtc)); the web app would send the browser there."
} catch {
    if ([int]$_.Exception.Response.StatusCode -ne 503) { throw }
    Write-Host "   HTTP 503: Stripe isn't configured (no STRIPE_SECRET_KEY in .env), so the Stripe checks are skipped."
}

Write-Host ""
Write-Host "6. Cancelling the confirmed order (compensating actions: Catalog gives the stock back, Payments closes the page)"
$stockBefore = (Invoke-Api GET "/api/products/$KeyboardId").Body.availableStock
$cancelled = (Invoke-Api POST "/api/orders/$($good.Body.id)/cancel" -Token $customerToken).Body
Write-Host "   order is $($cancelled.status); keyboards in stock before the release: $stockBefore"
$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Milliseconds 500
    $stockAfter = (Invoke-Api GET "/api/products/$KeyboardId").Body.availableStock
    $notes = @((Invoke-Api GET "/api/notifications?orderId=$($good.Body.id)" -Token $customerToken).Body)
} while (($stockAfter -lt $stockBefore + 1 -or $notes.Count -lt 3) -and (Get-Date) -lt $deadline)
Write-Host "   keyboards in stock after the release:  $stockAfter"
Write-Host "   emails for this order: $($notes.Count) (latest: '$($notes[0].subject)')"
$cancelOk = $cancelled.status -eq 'Cancelled' -and $stockAfter -eq $stockBefore + 1 -and $notes.Count -ge 3

$paymentOk = $true
if ($stripeConfigured) {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 500
        $attempt = @((Invoke-Api GET "/api/payments?orderId=$($good.Body.id)" -Token $customerToken).Body)[0]
    } while ($attempt.status -ne 'Cancelled' -and (Get-Date) -lt $deadline)
    Write-Host "   payment attempt: $($attempt.status) (Payments expired the Stripe page)"
    $paymentOk = $attempt.status -eq 'Cancelled'
}

Write-Host ""
Write-Host "7. Role checks"
$checks = @(
    [pscustomobject]@{ Check = 'Anonymous lists orders';       Expected = 401; Actual = (Get-StatusCode GET '/api/orders') }
    [pscustomobject]@{ Check = 'Customer restocks a product';  Expected = 403; Actual = (Get-StatusCode POST "/api/products/$KeyboardId/restock" @{ quantity = 1 } $customerToken) }
    [pscustomobject]@{ Check = 'Customer lists own orders';    Expected = 200; Actual = (Get-StatusCode GET '/api/orders' -Token $customerToken) }
    [pscustomobject]@{ Check = 'Admin lists all orders';       Expected = 200; Actual = (Get-StatusCode GET '/api/orders' -Token $adminToken) }
    [pscustomobject]@{ Check = 'Anonymous starts a payment';   Expected = 401; Actual = (Get-StatusCode POST '/api/payments/checkout' @{ orderId = $good.Body.id }) }
    [pscustomobject]@{ Check = "Admin pays a customer's order"; Expected = 403; Actual = (Get-StatusCode POST '/api/payments/checkout' @{ orderId = $good.Body.id } $adminToken) }
    [pscustomobject]@{ Check = 'Customer pays a cancelled order'; Expected = 409; Actual = (Get-StatusCode POST '/api/payments/checkout' @{ orderId = $good.Body.id } $customerToken) }
)
$checks | Format-Table -AutoSize | Out-String | Write-Host
$rolesOk = @($checks | Where-Object { $_.Expected -ne $_.Actual }).Count -eq 0

$passed = $confirmedOk -and $cancelOk -and $paymentOk -and $rolesOk

Write-Host "Follow the confirmed order through every service in the Aspire Dashboard:"
Write-Host "   http://localhost:18888/traces/detail/$($good.TraceId)"
Write-Host "   (or open http://localhost:18888, Traces, and filter by that trace id)"
Write-Host ""
if ($passed) {
    $stripeNote = if ($stripeConfigured) { 'payment page' } else { 'payments skipped (no Stripe key)' }
    Write-Host "PASS: confirmed + rejected + notifications + $stripeNote + cancel + roles, all through the gateway." -ForegroundColor Green
    exit 0
}
Write-Host "FAIL: order flow ok = $confirmedOk ('$($goodFinal.status)', '$($badFinal.status)'), cancel ok = $cancelOk, payment ok = $paymentOk, roles ok = $rolesOk." -ForegroundColor Red
exit 1
