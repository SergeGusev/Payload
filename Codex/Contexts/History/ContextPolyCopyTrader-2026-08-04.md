## 2026-08-04T05:46:39.6768549Z
Request:
Выложил, проверяй

Result:
Verified production deployment strictly read-only against exact primary `192.168.0.101:5432/polycopytrader`. The service is healthy on exact commit `014bc3de794df01fac545a4727f3f4d28975aa9a`, which contains removal of the modern schema declaration for `ix_paper_positions_wallet_updated`; heartbeat advanced across multiple one-minute periods, status stayed `Running / Live`, and `last_error` stayed null. The still-present target index was not rebuilt, remains valid at OID/relfilenode `133896`, occupies `4,330,291,200` bytes, and still has zero scans/no last scan. All nine retained Paper, Live, and Live-shadow history tables preserved their exact pre-deploy OID/relfilenode and had delete delta zero under the unchanged statistics epoch; Paper activity continued, and one indexed canary per table remained present. No long transaction, active index build, or target lock wait existed. No production or source state was changed. Before any separately approved concurrent drop, a fresh server-side process/service inventory is still required because PostgreSQL cannot conclusively exclude a dormant legacy process.

## 2026-08-04T06:43:53.4017905Z
Request:
PS C:\WINDOWS\system32> Get-CimInstance Win32_Process |
>>   Where-Object {
>>     $_.Name -in @(
>>       'PolyCopyTrader.Service.exe',
>>       'PolyCopyTrader.Net48.Service.exe'
>>     )
>>   } |
>>   Select-Object ProcessId, Name, ExecutablePath, CreationDate
>>
>> Get-CimInstance Win32_Service |
>>   Where-Object {
>>     $_.Name -in @('PolyCopyTrader.Service', 'PolyCopyTrader.Net48') -or
>>     $_.PathName -match 'PolyCopyTrader'
>>   } |
>>   Select-Object Name, State, StartMode, ProcessId, PathName

ProcessId Name                       ExecutablePath                                       CreationDate
--------- ----                       --------------                                       ------------
     8696 PolyCopyTrader.Service.exe C:\PayLoad\PayloadService\PolyCopyTrader.Service.exe 8/4/2026 8:30:54 AM

PS C:\WINDOWS\system32>

Result:
Accepted the bounded host process gate: exactly one known modern process was returned, no known Net48 process was returned, and the service query returned no matching row. The process creation time corresponds to `05:30:54Z`, approximately `2.75s` before the exact modern database heartbeat start. Ran the final production preflight read-only: exact primary/build/heartbeat remained healthy; the single `4.03 GiB` target retained exact OID/relfilenode `133896`, definition, ownership, zero scans, and zero constraints/dependencies/partition links; no index build, prepared transaction, long transaction, vacuum, active retention transfer, or waiting relation lock existed. All nine retained Paper/Live/live-shadow tables retained identity with delete delta zero and their canaries remained present; a settled non-skipped run canary was added for the durable betting-history gate. No production or source state changed. The only remaining gate is fresh explicit approval for exactly `DROP INDEX CONCURRENTLY public.ix_paper_positions_wallet_updated;`.
