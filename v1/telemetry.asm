.code
GetCpuCycles proc
    rdtsc           ; Read time-stamp counter into EDX:EAX
    shl rdx, 32     ; Shift EDX into upper 32 bits
    or rax, rdx     ; Combine into single 64-bit value in RAX
    ret
GetCpuCycles endp
end