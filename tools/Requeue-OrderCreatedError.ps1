<#
Requeue-OrderCreatedError.ps1

Pull messages from the RabbitMQ management API queue `order-created-queue_error`
and republish them to `order-created-queue`.

Usage (PowerShell 7+ or Windows PowerShell):
  .\tools\Requeue-OrderCreatedError.ps1 -Host "http://localhost:15672" -User guest -Pass guest -BatchSize 20
#>
param(
  [string] $HostUrl = "http://localhost:15672",
  [string] $VHost = "/",
  [string] $ErrorQueue = "order-created-queue_error",
  [string] $DestQueue = "order-created-queue",
  [string] $User = "guest",
  [string] $Pass = "guest",
  [int] $BatchSize = 20
)

function Encode-VHost($v) { if ($v -eq "/") { "%2F" } else { [System.Uri]::EscapeDataString($v) } }

$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("$User`:$Pass"))
$v = Encode-VHost $VHost
$apiGetUrl = "$HostUrl/api/queues/$v/$ErrorQueue/get"
$apiPublishUrl = "$HostUrl/api/exchanges/$v/amq.default/publish"

Write-Host "Requeueing from '$ErrorQueue' -> '$DestQueue' (batch $BatchSize)..."

while ($true) {
  $req = @{ count = $BatchSize; ackmode = "ack_requeue_false"; encoding = "auto"; truncate = 500000 } | ConvertTo-Json
  try {
    $msgs = Invoke-RestMethod -Method Post -Uri $apiGetUrl -Headers @{ Authorization = "Basic $auth" } -ContentType "application/json" -Body $req -ErrorAction Stop
  }
  catch {
    Write-Error "Failed to query management API: $_"
    break
  }

  if (-not $msgs -or $msgs.Count -eq 0) { Write-Host "No more messages."; break }

  foreach ($m in $msgs) {
    $encoding = if ($null -ne $m.payload_encoding -and $m.payload_encoding -eq "base64") { "base64" } else { "string" }

    $pub = @{
      properties = @{}
      routing_key = $DestQueue
      payload = $m.payload
      payload_encoding = $encoding
    }

    if ($m.properties) {
      if ($m.properties.headers) { $pub.properties.headers = $m.properties.headers }
      foreach ($p in @("content_type","correlation_id","message_id","type","timestamp","expiration")) {
        if ($m.properties.$p) { $pub.properties.$p = $m.properties.$p }
      }
    }

    $pubJson = $pub | ConvertTo-Json -Depth 10
    try {
      $resp = Invoke-RestMethod -Method Post -Uri $apiPublishUrl -Headers @{ Authorization = "Basic $auth" } -ContentType "application/json" -Body $pubJson -ErrorAction Stop
    }
    catch {
      Write-Warning "Failed to publish message id:$($m.properties.message_id -or '<no id>'): $_"
      continue
    }

    if ($resp.routed) { Write-Host "Republished id:" ($m.properties.message_id -or "<no id>") }
    else { Write-Warning "Publish not routed for id:" ($m.properties.message_id -or "<no id>") }
  }
}
Write-Host "Done."