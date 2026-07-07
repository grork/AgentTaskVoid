# Purpose & Motivation
This repo contains a project to wrap the AppTaskInfo API in a CLI that can be
consumed by other applications & scripts to help provide visibility into what
the scripts and applications are doing without having to monitor the application
CLI/script/app ui. Full docs for the API are available locally in the docs
folder.

At it's core, the AppTaskInfo APi allows an application to post state of an
operation -- progress, running, line-by-line status, paused, requesting
attention/input -- and it's mostly stateless. One of the key point is that it
*doesn't* require the originating process to be running for the last-seen-state
to be displayed. There are lots of nuances in the API (see the docs above) but
this is the general gist. But one of the challenges is that 'call it from a
script', or 'call it from an agent hook' is somewhat problematic, and requires
the api caller to do a lot of house keeping. It should be easy leverage this
from scripts et al with simple CLIs -- a wrapper for the API as a baseline
expectation.

The catalysing scenario was "add hook scripts into agents such as claude code,
github copilot cli, et al, and have them display status on the taskbar using
this API, allow handling of tool requests etc etc". the idea being that adding
them *directly* to these apps requires access to those tools, and since I don't
work at either anthropic or github isn't viable. Plus, there are so many agent
hosts that this wouldn't scale. When you include the script scenario -- where
scripts call this tool to display status -- it feels broadly useful.

It started out with a 'proof of concept' in this repo and it's now a functional
proof of concept, and it needs to be broadened out through a full implementation
covering real world scenarios, failure cases, testing, good ergonomics, etc.

## Assertions
1. Testability should follow 'write tests, see red, implement product code, see
   green, iterate' as our testing approach
2. We should be distribution-system-agnostic -- MSIX, loose file, all should be
   possible, but a late-bound decision for us. We'll revisit *after* we've
   completed building the product.

Here are some questions I've had rattling around in my head. These are **not**
exhaustive, nor in priority or other order. They are grouped by broad topic:

## Lifecycle
1. Chat sessions in an agent will have various signals, but it's not always
   guaranteed to terminate the session cleanly (E.g. call a clean up). There are
   also long periods of 'no activity' that might indicate either the session is
   end, or it's just taking time to learn. Since the API has no intrinsic
   timeouts etc, how can we model a heart beat to handle these failure mode.
   Should we have a mode in this system that will spin up a 'parent' process
   that persists in the background and gets 'debounced' for an idle period, and
   if it receives no updates in that time period remove the AppTaskInfo? What
   are the implications for this process -- do we need IPC? should we take an
   approach with a 'watch' directory that the CLI writes to, and the background
   process monitors? What is the most 'resilient', while increasing debugability
   & keeping the implementation simple?
2. Consider the behaviour of hooks between claude code, github copilot cli,
   codex, etc. Can we cover all of those scenarios?
3. If none of the hooks give us a sufficiently broad view and we end up with
   weird hacks should we consider (oh god) also sitting on the wire transport to
   the backend, and building knowledge of those transactions to add more signal?
   (this seems very bad, and only a last resort if any other hook-based approach
   is untenable)
## Usage Ergonomics
1. How should the cli ensure things can be updated/changed/revised by the caller
   of this CLI tool? What is the sharing contract for results that need to be
   maintained overtime -- return code that is an ID that the CLI itself
   maintains to allow itself to locate the right app task to clean up?
2. How / can we / should we handle 'garbage collection' of orphaned AppTaskInfo
   from us -- should we enumerate on every invocation and clean up the ones that
   have been hidden by the user (seems sensible)?
3. How should we express the different types of input here? My intuition says
   that 'some parameters -- possibly many -- to get the right outcome' is the
   best solution, but I'm anxious of creating something ergonomic for users
   given some of the restrictions (See docs folder)
4. How do we handle the fact that there are multiple consumers of the tool, and
   they don't/won't want them to all 'glom' together into a single icon you have
   to hover over. And there maybe scenarios where they *do* want that glomming!
   This is about putting the control into the consumers hands, and not being too
   opinionated here.
5. Should we provide a 'wrapper' around scripts/other command line tools? E.g.
   invoke the other script/tool, and read the stdout/stderr, and manage the life
   cycle of the apptaskinfo through those states -- putting lines into the
   AppTaskContent sequence?

## Infrastructure / Programming paradigm
1. Are there / should there be concurrency issues here that maybe the API wasn't
   originally considering that because we're 'brokering' all the calls will
   trigger a bunch of problems/race conditions from within the inproc api
   (OSClient.API.dll hosts the api in proc in the calling process; explorer.exe
   separately watches for file changes and applies what it sees)
2. How can we minimize the on-disk size of our tool? Managed code is easy to
   read, but either ends up with very large binaries when merged into a 'single
   exe' (10s or 100s of mb), or has many dll dependencies in the folder which
   makes it difficult to share. This is somewhat related to the distribution
   system, but it's foundational and has knock on implications. I want people to
   get it quickly, and just rock it.
3. Should we consider writing the entire tool in C++ or Rust to ensure the
   on-disk size is very small? I *deeply* care about the readability,
   understandability, and 'easy of working on' in the code, but depending on the
   full depth of complexity of the code to support the scenarios we need, maybe
   the actual code is narrow.
4. What should testability look like at a code level -- how should we architect
   / split components to enable our testability aims? If we move beyond a simple
   wrapper that doesn't maintain state itself (difficult, I suspect), I want the
   tests (unit, integration) etc to be vast, and cover as many scenarios as
   possible. I also want to enable agents to 'hill climb' with as much non-human
   intervention as much as possible -- sure, with the actual taskbar itself,
   that might be hard, but we have `tasks.json` to verify state, et al for when
   we cross into 'integration' territory.

What other questions am I missing to help get to a great & detailed plan? Once
we have a plan -- which can involve resolving questions by experimentation, but
which should cover off the paths depending on the outputs of those experiments
-- i'd like to add it into the repo in a specific folder (plans), but only once
we're all completely buttoned up.