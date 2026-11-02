#define UNICODE
#define _UNICODE
#include <windows.h>
#include <stdio.h>
static HMENU menu;
static int originalClicks;
static LRESULT CALLBACK WindowProc(HWND hwnd,UINT message,WPARAM wp,LPARAM lp) {
    if(message==WM_APP+100) {
        if(menu) DestroyMenu(menu);
        menu=CreatePopupMenu();
        AppendMenuW(menu,MF_STRING,1001,L"原来的保持唤醒");
        AppendMenuW(menu,MF_STRING,1002,L"原来的屏幕选项");
        return (LRESULT)menu;
    }
    if(message==WM_APP+101) return (LRESULT)menu;
    if(message==WM_APP+102) return originalClicks;
    if(message==WM_APP+103) { DestroyWindow(hwnd); return 0; }
    if(message==WM_APP+104) return GetModuleHandleW(L"AwakeLidHook.dll")!=NULL;
    if(message==WM_COMMAND) { originalClicks++; return 0; }
    if(message==WM_DESTROY) { if(menu) DestroyMenu(menu); PostQuitMessage(0); return 0; }
    return DefWindowProcW(hwnd,message,wp,lp);
}
int main(int argc,char**argv) {
    (void)argv;
    HINSTANCE instance=GetModuleHandleW(NULL);
    WNDCLASSW cls={0}; cls.lpfnWndProc=WindowProc; cls.hInstance=instance;
    cls.lpszClassName=argc>1?L"Unsupported.MockWindow":L"Awake.MessageWindow";
    RegisterClassW(&cls);
    HWND hwnd=CreateWindowW(cls.lpszClassName,L"AwakeLid hidden test host",0,0,0,0,0,NULL,NULL,instance,NULL);
    printf("%lu %llu\n",GetCurrentProcessId(),(unsigned long long)(UINT_PTR)hwnd); fflush(stdout);
    MSG msg;
    while(GetMessageW(&msg,NULL,0,0)>0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    return 0;
}
