#define UNICODE
#define _UNICODE
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif
#include <windows.h>
#include <commctrl.h>
#include <stdio.h>
#include "bridge.h"

static AL_SHARED *g_state;
static HANDLE g_mapping, g_guardian;
static HMODULE g_self;
static HWND g_window;
static HMENU g_root, g_sub;
static UINT g_base, g_ping;
static BOOL g_expectRoot;
static UINT_PTR g_timer;
static LRESULT CALLBACK Subclass(HWND, UINT, WPARAM, LPARAM, UINT_PTR, DWORD_PTR);

static void RecoverOrphan(void) {
    if (!g_guardian || WaitForSingleObject(g_guardian,0)!=WAIT_OBJECT_0 || !g_state || (g_state->status&0xff)<2) return;
    WCHAR executable[4096],command[4200];
    DWORD n=GetModuleFileNameW(g_self,executable,4096);
    if (!n || n>=4096) return;
    while(n && executable[n-1]!=L'\\') --n;
    if (!n || n+13>=4096) return;
    lstrcpyW(executable+n,L"AwakeLid.exe");
    if(GetFileAttributesW(executable)==INVALID_FILE_ATTRIBUTES) return;
    wsprintfW(command,L"\"%s\" --recover-once",executable);
    STARTUPINFOW startup={0}; startup.cb=sizeof(startup); startup.dwFlags=STARTF_USESHOWWINDOW; startup.wShowWindow=SW_HIDE;
    PROCESS_INFORMATION process={0};
    if(CreateProcessW(executable,command,NULL,NULL,FALSE,CREATE_NO_WINDOW,NULL,NULL,&startup,&process)) {
        CloseHandle(process.hThread); CloseHandle(process.hProcess);
    }
}

static DWORD WINAPI ReleaseSelf(void *module) {
    Sleep(250); // Return from the subclass and allow the controller to unhook first.
    FreeLibraryAndExitThread((HMODULE)module, 0);
    return 0;
}
static void RemoveItems(void) {
    if (g_root && IsMenu(g_root) && g_base) {
        RemoveMenu(g_root, g_base + 4, MF_BYCOMMAND);
        RemoveMenu(g_root, g_base + 5, MF_BYCOMMAND);
    }
    if (g_sub && IsMenu(g_sub)) DestroyMenu(g_sub);
    g_root = g_sub = NULL;
}
static BOOL Alive(void) {
    return g_state && g_state->enabled && !g_state->detach &&
        (DWORD)(GetTickCount() - (DWORD)g_state->guardianTick) < 8000 &&
        g_guardian && WaitForSingleObject(g_guardian, 0) == WAIT_TIMEOUT;
}
static void Detach(void) {
    RemoveItems();
    if (g_window) {
        if (g_timer) KillTimer(g_window, g_timer);
        RemoveWindowSubclass(g_window, Subclass, AL_SUBCLASS);
    }
    if (g_state) { g_state->attached = 0; g_state->hookTick = GetTickCount(); UnmapViewOfFile(g_state); }
    if (g_mapping) CloseHandle(g_mapping);
    if (g_guardian) CloseHandle(g_guardian);
    HMODULE self = g_self;
    g_state = NULL; g_mapping = g_guardian = NULL; g_self = NULL; g_window = NULL; g_timer = 0;
    if (self) {
        HANDLE thread = CreateThread(NULL, 0, ReleaseSelf, self, 0, NULL);
        if (thread) CloseHandle(thread); // On rare allocation failure retain the reference, never unload active code.
    }
}
static BOOL ContainsId(HMENU menu, UINT id) {
    int count = GetMenuItemCount(menu);
    for (int i=0; i<count; ++i) {
        if (GetMenuItemID(menu, i) == id) return TRUE;
        HMENU sub = GetSubMenu(menu, i);
        if (sub && ContainsId(sub, id)) return TRUE;
    }
    return FALSE;
}
static void MenuError(DWORD code) {
    if (g_state) g_state->error = code ? (LONG)code : ERROR_INVALID_DATA;
    RemoveItems();
}
static void AddMenu(HMENU root) {
    if (!Alive() || !IsMenu(root)) return;
    RemoveItems();
    g_root = root;
    UINT base;
    for (base=0xE700; base<0xEF00; base+=8) {
        BOOL used=FALSE;
        for (UINT i=0; i<8; ++i) if (ContainsId(root, base+i)) used=TRUE;
        if (!used) break;
    }
    if (base>=0xEF00) { MenuError(ERROR_NO_SYSTEM_RESOURCES); return; }
    g_base=base;
    g_sub=CreatePopupMenu();
    if (!g_sub) { MenuError(GetLastError()); return; }
    DWORD mask=(DWORD)g_state->reserved;
    UINT disabled=(g_state->status&AL_AWAKE_ENABLED)?0:MF_GRAYED;
    BOOL ok=AppendMenuW(g_sub, MF_STRING|MF_GRAYED, base, L"跟随 Awake") &&
        AppendMenuW(g_sub, MF_STRING|disabled|((mask&1)?MF_CHECKED:0), base+1, L"    接通电源时") &&
        AppendMenuW(g_sub, MF_STRING|disabled|((mask&2)?MF_CHECKED:0), base+2, L"    使用电池时") &&
        AppendMenuW(g_sub, MF_SEPARATOR, 0, NULL) &&
        AppendMenuW(g_sub, MF_STRING|(mask==0?MF_CHECKED:0), base+3, L"跟随系统设置");
    const WCHAR *status=L"状态：等待 Awake 开始";
    switch(g_state->status&0xff) {
        case 0: status=L"状态：跟随系统设置"; break;
        case 2: status=L"状态：随 Awake 生效中"; break;
        case 3: status=L"状态：已手动调整，部分联动暂停"; break;
        case 4: status=L"状态：异常，查看外挂状态"; break;
    }
    ok=ok && AppendMenuW(g_sub,MF_SEPARATOR,0,NULL) &&
        AppendMenuW(g_sub,MF_STRING|MF_GRAYED,0,status) &&
        AppendMenuW(g_sub,MF_STRING,base+6,L"查看外挂状态") &&
        AppendMenuW(g_sub,MF_STRING,base+7,L"打开系统合盖设置");
    MENUITEMINFOW sep={0}; sep.cbSize=sizeof(sep); sep.fMask=MIIM_FTYPE|MIIM_ID; sep.fType=MFT_SEPARATOR; sep.wID=base+5;
    MENUITEMINFOW item={0}; item.cbSize=sizeof(item); item.fMask=MIIM_ID|MIIM_STRING|MIIM_SUBMENU;
    item.wID=base+4; item.hSubMenu=g_sub; item.dwTypeData=L"合盖保持唤醒";
    ok=ok && InsertMenuItemW(root,(UINT)-1,TRUE,&sep) && InsertMenuItemW(root,(UINT)-1,TRUE,&item);
    if (!ok) { MenuError(GetLastError()); return; }
    g_state->error=0;
    InterlockedIncrement(&g_state->menuSeen);
}
static LRESULT CALLBACK Subclass(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp, UINT_PTR id, DWORD_PTR data) {
    (void)id; (void)data;
    if (!Alive()) { RecoverOrphan(); Detach(); return DefSubclassProc(hwnd,msg,wp,lp); }
    g_state->hookTick=GetTickCount();
    if (msg==g_ping) return AL_MAGIC;
    if (msg==WM_ENTERMENULOOP) g_expectRoot=TRUE;
    if (msg==WM_INITMENUPOPUP && g_expectRoot && !HIWORD(lp)) {
        g_expectRoot=FALSE;
        LRESULT result=DefSubclassProc(hwnd,msg,wp,lp);
        AddMenu((HMENU)wp);
        return result;
    }
    if (msg==WM_COMMAND && HIWORD(wp)==0 && lp==0 && g_base) {
        UINT command=LOWORD(wp);
        LONG action=command==g_base+1?1:command==g_base+2?2:command==g_base+3?3:
            command==g_base+6?4:command==g_base+7?5:0;
        if (action) {
            if (action<=2 && !(g_state->status&AL_AWAKE_ENABLED)) return 0;
            if (action<=3) {
                if (action==1) InterlockedXor(&g_state->reserved,1);
                else if (action==2) InterlockedXor(&g_state->reserved,2);
                else InterlockedExchange(&g_state->reserved,0);
                action=256+(g_state->reserved&3);
            }
            InterlockedExchange(&g_state->command,action);
            InterlockedIncrement(&g_state->commandSeq);
            return 0;
        }
    }
    if (msg==WM_EXITMENULOOP) RemoveItems();
    if (msg==WM_NCDESTROY) { Detach(); return DefSubclassProc(hwnd,msg,wp,lp); }
    return DefSubclassProc(hwnd,msg,wp,lp);
}
static void TryAttach(HWND hwnd) {
    if (g_state) return;
    WCHAR className[128];
    if (!GetClassNameW(hwnd,className,128) || lstrcmpW(className,L"Awake.MessageWindow")) return;
    DWORD pid; DWORD tid=GetWindowThreadProcessId(hwnd,&pid);
    if (pid!=GetCurrentProcessId() || tid!=GetCurrentThreadId()) return;
    WCHAR name[96]; wsprintfW(name,L"Local\\AwakeLid.v1.%lu",pid);
    HANDLE mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name);
    if (!mapping) return;
    AL_SHARED *state=MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(AL_SHARED));
    if (!state) { CloseHandle(mapping); return; }
    if (state->magic!=AL_MAGIC || state->protocol!=AL_PROTOCOL || state->targetPid!=pid || !state->enabled || state->detach) {
        UnmapViewOfFile(state); CloseHandle(mapping); return;
    }
    g_guardian=OpenProcess(SYNCHRONIZE,FALSE,state->guardianPid);
    g_mapping=mapping; g_state=state;
    if (!Alive()) { Detach(); return; }
    // A balanced reference keeps callbacks valid even if the guardian unexpectedly exits.
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,(LPCWSTR)&TryAttach,&g_self)) { Detach(); return; }
    g_ping=RegisterWindowMessageW(L"AwakeLid.Ping.v1");
    if (!SetWindowSubclass(hwnd,Subclass,AL_SUBCLASS,0)) { state->error=GetLastError(); Detach(); return; }
    g_window=hwnd;
    g_timer=SetTimer(hwnd,AL_SUBCLASS,1000,NULL);
    state->attached=1; state->hookTick=GetTickCount();
}
__declspec(dllexport) LRESULT CALLBACK AwakeLidHook(int code, WPARAM wp, LPARAM lp) {
    if (code>=0) { CWPSTRUCT *message=(CWPSTRUCT*)lp; TryAttach(message->hwnd); }
    return CallNextHookEx(NULL,code,wp,lp);
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID reserved) {
    (void)reserved;
    if (reason==DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(module);
    return TRUE;
}
