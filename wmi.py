import win32com.client
import time
import os

def run_full_dump():
    output_file = "wmi_live_log.txt"
    print(f"Starting hardware recon. Output will be saved to {output_file} and printed below.\n")

    # Initialize the log file with the requested paragraph comment
    with open(output_file, "w", encoding="utf-8") as f:
        f.write("=== LOQ HARDWARE LOG ===\n")
        f.write("/*\n")
        f.write("  UNNEEDED / UNSUPPORTED DATA NOTES:\n")
        f.write("  Many WMI endpoints return 0 because the LOQ motherboard does not physically support them.\n")
        f.write("  Examples: GetWaterCoolingStatus, GetMacrokeyCount, GetGSyncStatus, GetTPStatus.\n")
        f.write("  These can be ignored. We are primarily looking for the Recon Data below.\n")
        f.write("*/\n\n")

    # Helper function to guarantee we see the output in both places instantly
    def write_and_print(text):
        print(text)
        with open(output_file, "a", encoding="utf-8") as f:
            f.write(text + "\n")

    try:
        wmi = win32com.client.GetObject(r"winmgmts:\\.\root\wmi")
    except Exception as e:
        write_and_print(f"❌ Error connecting to WMI: {e}")
        return

    # --- RECON DUMP ---
    write_and_print("--- RECON: FAN RPM DATA ---")
    try:
        fan_data = wmi.InstancesOf("LENOVO_FAN_MAX_SPEED_DATA")
        if fan_data.Count > 0:
            for instance in fan_data:
                write_and_print(f"Fan ID: {instance.Fan_Id} | Max Speed: {instance.Fan_CurrentMaxSpeed}")
        else:
            write_and_print("No Fan RPM data found.")
    except Exception as e:
        write_and_print(f"Fan query failed: {e}")

    write_and_print("\n--- RECON: KEYBOARD LIGHTING ---")
    try:
        lighting_instances = wmi.InstancesOf("LENOVO_LIGHTING_METHOD")
        if lighting_instances.Count > 0:
            light_sys = next(iter(lighting_instances))
            for i in range(5):
                try:
                    in_params = light_sys.Methods_("Get_Lighting_Current_Status").InParameters.SpawnInstance_()
                    in_params.Properties_("Lighting_ID").Value = i
                    out_params = light_sys.ExecMethod_("Get_Lighting_Current_Status", in_params)
                    b = out_params.Properties_("Current_Brightness_Level").Value
                    s = out_params.Properties_("Current_State_Type").Value
                    write_and_print(f"Lighting_ID {i} is ACTIVE -> Brightness: {b}, State: {s}")
                except Exception:
                    pass
        else:
            write_and_print("No Lighting instance found.")
    except Exception as e:
        write_and_print(f"Lighting query failed: {e}")

    write_and_print("\n--- LIVE GAMEZONE POLLING ---")
    write_and_print("Press Fn+Q or change settings in Vantage to see changes. Press Ctrl+C to stop.\n")

    # --- LIVE POLLING SETUP ---
    try:
        class_def = wmi.Get("LENOVO_GAMEZONE_DATA")
        safe_methods = sorted([
            m.Name for m in class_def.Methods_ 
            if m.InParameters is None or m.InParameters.Properties_.Count == 0
        ])
        gamezone = next(iter(wmi.InstancesOf("LENOVO_GAMEZONE_DATA")))
    except Exception as e:
        write_and_print(f"Error setting up live poll: {e}")
        return

    previous_results = {}
    
    # --- LIVE POLLING EXECUTION ---
    try:
        while True:
            results = {}
            for method_name in safe_methods:
                try:
                    out_params = gamezone.ExecMethod_(method_name)
                    out_values = [str(prop.Value) for prop in out_params.Properties_] if out_params else []
                    results[method_name] = ", ".join(out_values) if out_values else "No Output"
                except Exception:
                    results[method_name] = "Error"

            # Append to file and print only if a value changes
            if results != previous_results:
                timestamp = time.strftime('%Y-%m-%d %H:%M:%S')
                header = f"\n--- Change detected at {timestamp} ---"
                print(header)
                with open(output_file, "a", encoding="utf-8") as f:
                    f.write(header + "\n")
                    
                for name, value in results.items():
                    line = f"{name.ljust(30)} : {value}"
                    print(line)
                    with open(output_file, "a", encoding="utf-8") as f:
                        f.write(line + "\n")
                
                previous_results = results.copy()

            time.sleep(1)
            
    except KeyboardInterrupt:
        print(f"\nHardware polling stopped. Log saved to {output_file}.")

if __name__ == "__main__":
    run_full_dump()