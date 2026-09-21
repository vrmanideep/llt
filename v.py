import win32com.client

try:
    print("Querying Windows WMI Kernel...")
    
    wmi = win32com.client.GetObject(r"winmgmts:\\.\root\wmi")
    hardware = wmi.InstancesOf("LENOVO_OTHER_METHOD")[0]
    
    test_id = 33881856
    
    # Construct the input parameters for GetFeatureValue
    method = hardware.Methods_("GetFeatureValue")
    in_params = method.InParameters.SpawnInstance_()
    in_params.Properties_.Item("IDs").Value = int(test_id)
    
    # Execute the read command
    out_params = hardware.ExecMethod_("GetFeatureValue", in_params)
    
    # Extract the returned value
    returned_value = out_params.Properties_.Item("value").Value
    
    print("--- RESULT ---")
    print(f"Motherboard reports ID {test_id} is currently set to: {returned_value}")

except Exception as e:
    print(f"QUERY FAILED: {e}")