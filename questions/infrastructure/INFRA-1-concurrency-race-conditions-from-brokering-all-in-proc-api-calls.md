# INFRA-1: Concurrency / race conditions from brokering all in-proc API calls
**Status:** EXPANDED
**Expanded into:** INFRA-5, INFRA-6, INFRA-7

Are there / should there be concurrency issues here that maybe the API wasn't
originally considering, that because we're 'brokering' all the calls will
trigger a bunch of problems/race conditions from within the in-proc API?
(OSClient.API.dll hosts the API in-proc in the calling process; explorer.exe
separately watches for file changes and applies what it sees.)
