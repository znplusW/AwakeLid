#pragma once
#include <windows.h>
#define AL_MAGIC 0x414C4944u
#define AL_PROTOCOL 2u
#define AL_AWAKE_ENABLED 0x100u
#define AL_SUBCLASS 0x414C4944u
typedef struct {
    DWORD magic, protocol, guardianPid, targetPid;
    volatile LONG selection, status, command, commandSeq;
    volatile LONG guardianTick, hookTick, attached, menuSeen;
    volatile LONG error, enabled, detach, reserved;
} AL_SHARED;
// 64 bytes, all naturally aligned 32-bit fields, mirrored by HookBridge.cs.
