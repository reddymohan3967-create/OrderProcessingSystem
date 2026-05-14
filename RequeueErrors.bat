@echo off
cd /d "C:\Users\LENOVO\source\repos\OrderProcessingSystem\OrderProcessingSystem"

powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\Requeue-OrderCreatedError.ps1" -Host "http://localhost:15672" -User guest -Pass guest -BatchSize 5

pause