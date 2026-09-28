# Security Policy

## What SMARTProbe is, from a security point of view

SMARTProbe is a **privileged process that answers unauthenticated HTTP requests**. Understand
that before deploying it.

- The recommended install runs it as a `LocalSystem` Windows service.
- The API has **no authentication**. Anyone who can reach the socket can read every disk's model,
  capacity, health, temperature and error counters, and the machine name.
- It is **read-only**. There is no endpoint that writes, configures, or executes anything. The only
  mutable state is the service registration itself, which requires administrator rights and is done
  through `sc.exe` with arguments fixed at install time.
- It **binds to 127.0.0.1 by default**. Only processes on the same machine can reach it.

## `--bind any` is a deliberate exposure

Passing `--bind any` (at run time or at `--install` time) listens on every interface. Do this only
on a network you trust, and expect that anyone on it can read the data above. Put a reverse proxy
with authentication in front of it if you need remote access with any access control at all;
SMARTProbe will not grow an auth layer of its own — that belongs in the caller or the proxy.

## Threat model summary

| Threat | Position |
|---|---|
| Local unprivileged user reads hardware detail via the API | Accepted by design; that is the purpose of the probe |
| Remote user reads hardware detail | Prevented by default (loopback); opt-in via `--bind any` |
| Caller coerces the service into doing something else | Prevented: no writable endpoints; service arguments are fixed in the registered binPath |
| Denial of service by hammering `/probe/smart` (~300 ms WMI call each) | Not mitigated; loopback-only by default makes the attacker a local process, which has easier options |
| Tampered binary | Verify the SHA-256 published with each release before installing |

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please **do not open a public issue** for security problems.

Use GitHub's private vulnerability reporting on this repository
(*Security* → *Report a vulnerability*). If that is unavailable, email the maintainer at the address
on their GitHub profile with "SMARTProbe security" in the subject.

You will get an acknowledgement within 7 days. Fixes are released as soon as they are ready, with
credit to the reporter unless anonymity is requested.
