#define UNICODE
#define _UNICODE
#include <windows.h>
int main(void) {
    WCHAR path[4096];
    DWORD n=GetModuleFileNameW(NULL,path,4096);
    while(n && path[n-1]!=L'\\') --n;
    if(!n || n+32>=4096) return 1;
    lstrcpyW(path+n,L"orphan-recovery-test.txt");
    HANDLE file=CreateFileW(path,GENERIC_WRITE,FILE_SHARE_READ,NULL,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,NULL);
    if(file==INVALID_HANDLE_VALUE) return 2;
    DWORD written; WriteFile(file,"RECOVERY_CALLED",15,&written,NULL); CloseHandle(file);
    return 0;
}
