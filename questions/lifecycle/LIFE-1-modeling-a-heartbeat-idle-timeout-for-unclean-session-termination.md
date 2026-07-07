# LIFE-1: Modeling a heartbeat / idle timeout for unclean session termination
**Status:** EXPANDED
**Expanded into:** LIFE-4, LIFE-5, LIFE-6, LIFE-7

Chat sessions in an agent will have various signals, but it's not always
guaranteed to terminate the session cleanly (e.g. call a clean up). There are
also long periods of 'no activity' that might indicate either the session has
ended, or it's just taking time to think. Since the API has no intrinsic
timeouts etc, how can we model a heartbeat to handle these failure modes?

Should we have a mode in this system that will spin up a 'parent' process that
persists in the background and gets 'debounced' for an idle period, and if it
receives no updates in that time period removes the AppTaskInfo? What are the
implications for this process -- do we need IPC? Should we take an approach with
a 'watch' directory that the CLI writes to, and the background process monitors?
What is the most 'resilient', while increasing debuggability & keeping the
implementation simple?
