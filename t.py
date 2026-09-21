import win32com.client

try:
    print("Initializing Read-Only WMI Scanner...")
    wmi = win32com.client.GetObject(r"winmgmts:\\.\root\wmi")
    
    instances = wmi.InstancesOf("LENOVO_OTHER_METHOD")
    if len(instances) == 0:
        print("ERROR: Could not find LENOVO_OTHER_METHOD.")
        exit()
        
    hardware = instances[0]

    # These are all the hardware IDs we extracted from your config.ini
    safe_ids = [
        16900096, 16965632, 17031168, 17096704, 34332672, 17227776, 
        33677312, 33742848, 33808384, 33873920, 33881856, 16908032, 
        16973568, 17039104, 17104640, 34340608, 17235712, 33685248, 
        33750784, 33816320
    ]

    # In Lenovo ACPI, Command 1 or 2 typically means "Get/Read"
    read_commands = [1, 2] 
    
    # We know the fan curve requires a 10-point array 
    empty_array = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]

    print("Scanning for Fan Curve Data (Read-Only Mode)...")
    print("-" * 50)
    
    for cmd in read_commands:
        for test_id in safe_ids:
            try:
                method = hardware.Methods_("GetDataByCommand")
                in_params = method.InParameters.SpawnInstance_()
                
                in_params.Properties_.Item("Command").Value = int(cmd)
                in_params.Properties_.Item("IDs").Value = int(test_id)
                in_params.Properties_.Item("Data").Value = empty_array
                in_params.Properties_.Item("DataSize").Value = len(empty_array)
                
                # Execute the read command
                out_params = hardware.ExecMethod_("GetDataByCommand", in_params)
                returned_data = list(out_params.Properties_.Item("Data").Value)
                
                # If the motherboard returns an array that isn't just zeros, print it
                if sum(returned_data) > 0:
                    print(f"[FOUND DATA] Cmd: {cmd} | ID: {test_id} -> {returned_data}")
                    
            except Exception:
                # Silently ignore if the ID rejects array structures
                pass 

    print("-" * 50)
    print("Scan Complete.")

except Exception as e:
    print(f"Scanner Failed: {e}")