import os
import sys
import ctypes
import win32com.client
import pythoncom
import hid
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import uvicorn

# --- UAC Elevation Check ---
def is_admin():
    try: return ctypes.windll.shell32.IsUserAnAdmin()
    except: return False

if not is_admin():
    print("Requesting Administrator privileges...")
    ctypes.windll.shell32.ShellExecuteW(None, "runas", sys.executable, " ".join(sys.argv), None, 1)
    sys.exit()

# --- Battery Struct for powrprof.dll ---
class SYSTEM_BATTERY_STATE(ctypes.Structure):
    _fields_ = [
        ("AcOnLine", ctypes.c_byte),
        ("BatteryPresent", ctypes.c_byte),
        ("Charging", ctypes.c_byte),
        ("Discharging", ctypes.c_byte),
        ("Spare1", ctypes.c_byte * 3),
        ("Tag", ctypes.c_byte),
        ("MaxCapacity", ctypes.c_ulong),
        ("RemainingCapacity", ctypes.c_ulong),
        ("Rate", ctypes.c_long), 
        ("EstimatedTime", ctypes.c_ulong),
        ("DefaultAlert1", ctypes.c_ulong),
        ("DefaultAlert2", ctypes.c_ulong),
    ]

# --- Hardware Dictionaries ---
FAN_MODES = {"quiet": 1, "balanced": 2, "performance": 3, "custom": 255}
GPU_MODES = {"hybrid": 0, "dgpu": 1, "igpu": 2}

app = FastAPI(title="LOQ Hardware API")

class FanModeRequest(BaseModel):
    mode: str 

class GpuModeRequest(BaseModel):
    mode: str 

class ToggleRequest(BaseModel):
    enabled: bool

class RGBRequest(BaseModel):
    brightness: int  # 0 (Off), 1 (Low), 2 (High)
    effect: str      # "static" or "breathing"
    r: int           # 0-255
    g: int           # 0-255
    b: int           # 0-255

def get_wmi_instance(class_name):
    wmi = win32com.client.GetObject(r"winmgmts:\\.\root\wmi")
    instances = wmi.InstancesOf(class_name)
    if instances.Count == 0:
        raise Exception(f"{class_name} not found.")
    return next(iter(instances))

def send_ite_rgb_payload(brightness: int, effect_str: str, r: int, g: int, b: int):
    VID = 0x048D
    PID = 0xC693
    
    effects = {"static": 1, "breathing": 3}
    effect_id = effects.get(effect_str.lower(), 1)
    
    # Constructing the 33-byte ITE lighting payload
    payload = [0x00] * 33
    payload[0] = 0xCC  # Report ID
    payload[1] = 0x16  # Command ID
    payload[2] = effect_id
    payload[3] = 0x02  # Speed (2 = Medium)
    payload[4] = brightness
    
    # 4 RGB Zones (Writing same color to all zones)
    for zone in range(4):
        idx = 5 + (zone * 3)
        payload[idx] = r
        payload[idx+1] = g
        payload[idx+2] = b

    devices = hid.enumerate(VID, PID)
    if not devices:
        raise Exception("Keyboard RGB controller not found.")

    # Target the vendor-specific interface (Usage Page 65417)
    target_path = next((dev['path'] for dev in devices if dev['usage_page'] == 65417), devices[0]['path'])

    try:
        device = hid.device()
        device.open_path(target_path)
        try:
            device.send_feature_report(payload)
        except ValueError:
            # Fallback to standard Output Report if Feature Report is blocked
            device.write(payload)
        device.close()
    except Exception as e:
        raise Exception(f"HID Write Error: {str(e)}")

# --- Read-Only Telemetry ---
@app.get("/api/telemetry")
def get_telemetry():
    pythoncom.CoInitialize() 
    try:
        gamezone = get_wmi_instance("LENOVO_GAMEZONE_DATA")
        cpu_temp = gamezone.ExecMethod_("GetCPUTemp").Properties_("Data").Value
        gpu_temp = gamezone.ExecMethod_("GetGPUTemp").Properties_("Data").Value
        raw_freq = gamezone.ExecMethod_("GetCpuFrequency").Properties_("Data").Value
        fan_status = gamezone.ExecMethod_("GetFanCoolingStatus").Properties_("Data").Value

        freq_str = str(raw_freq)
        cpu_ghz = float(f"{freq_str[0]}.{freq_str[1:3]}") if len(freq_str) >= 2 else 0.0

        battery_data = {}
        sbs = SYSTEM_BATTERY_STATE()
        status = ctypes.windll.powrprof.CallNtPowerInformation(5, None, 0, ctypes.byref(sbs), ctypes.sizeof(sbs))
        
        if status == 0 and sbs.BatteryPresent:
            rate_mw = sbs.Rate
            if rate_mw > 2147483647:
                rate_mw -= 4294967296
            wattage = abs(rate_mw) / 1000.0  
            
            battery_data = {
                "plugged_in": bool(sbs.AcOnLine),
                "is_charging": bool(sbs.Charging),
                "is_discharging": bool(sbs.Discharging),
                "capacity_percent": int((sbs.RemainingCapacity / sbs.MaxCapacity) * 100) if sbs.MaxCapacity > 0 else 0,
                "current_wattage": round(wattage, 2)
            }

        return {
            "status": "online",
            "temperatures": {"cpu": cpu_temp, "gpu": gpu_temp},
            "clocks": {"cpu_frequency_ghz": cpu_ghz},
            "fans": {"is_active": True if fan_status > 0 else False},
            "battery": battery_data
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error: {str(e)}")
    finally:
        pythoncom.CoUninitialize()

@app.get("/api/state")
def get_hardware_state():
    pythoncom.CoInitialize()
    try:
        gamezone = get_wmi_instance("LENOVO_GAMEZONE_DATA")
        fan_int = gamezone.ExecMethod_("GetSmartFanMode").Properties_("Data").Value
        gpu_int = gamezone.ExecMethod_("GetIGPUModeStatus").Properties_("Data").Value
        od_int = gamezone.ExecMethod_("GetODStatus").Properties_("Data").Value

        return {
            "fan_mode": next((k for k, v in FAN_MODES.items() if v == fan_int), "unknown"),
            "gpu_mode": next((k for k, v in GPU_MODES.items() if v == gpu_int), "unknown"),
            "overdrive_enabled": True if od_int == 1 else False
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"WMI Error: {str(e)}")
    finally:
        pythoncom.CoUninitialize()

# --- Execution Endpoints ---
@app.post("/api/thermal")
def set_thermal_mode(request: FanModeRequest):
    mode_str = request.mode.lower()
    if mode_str not in FAN_MODES:
        raise HTTPException(status_code=400, detail="Invalid fan mode.")
    
    target_int = FAN_MODES[mode_str]
    pythoncom.CoInitialize()
    try:
        gamezone = get_wmi_instance("LENOVO_GAMEZONE_DATA")
        if target_int in [3, 255] and gamezone.ExecMethod_("IsACFitForOC").Properties_("Data").Value == 0:
            raise HTTPException(status_code=409, detail="Performance mode blocked on battery power.")
        
        in_params = gamezone.Methods_("SetSmartFanMode").InParameters.SpawnInstance_()
        in_params.Properties_("Data").Value = target_int
        gamezone.ExecMethod_("SetSmartFanMode", in_params)
        return {"status": "success", "mode": mode_str}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"WMI Error: {str(e)}")
    finally:
        pythoncom.CoUninitialize()

@app.post("/api/gpu")
def set_gpu_mode(request: GpuModeRequest):
    mode_str = request.mode.lower()
    if mode_str not in GPU_MODES:
        raise HTTPException(status_code=400, detail="Invalid GPU mode.")
    
    target_int = GPU_MODES[mode_str]
    pythoncom.CoInitialize()
    try:
        gamezone = get_wmi_instance("LENOVO_GAMEZONE_DATA")
        in_params = gamezone.Methods_("SetIGPUModeStatus").InParameters.SpawnInstance_()
        in_params.Properties_("mode").Value = target_int
        gamezone.ExecMethod_("SetIGPUModeStatus", in_params)
        return {"status": "success", "mode": mode_str, "reboot_required": True}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"WMI Error: {str(e)}")
    finally:
        pythoncom.CoUninitialize()

@app.post("/api/overdrive")
def set_overdrive(request: ToggleRequest):
    pythoncom.CoInitialize()
    try:
        gamezone = get_wmi_instance("LENOVO_GAMEZONE_DATA")
        in_params = gamezone.Methods_("SetODStatus").InParameters.SpawnInstance_()
        in_params.Properties_("Data").Value = 1 if request.enabled else 0
        gamezone.ExecMethod_("SetODStatus", in_params)
        return {"status": "success", "overdrive": request.enabled}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"WMI Error: {str(e)}")
    finally:
        pythoncom.CoUninitialize()

@app.post("/api/rgb")
def set_keyboard_rgb(request: RGBRequest):
    if request.brightness not in [0, 1, 2]:
        raise HTTPException(status_code=400, detail="Brightness must be 0, 1, or 2.")
    if not (0 <= request.r <= 255 and 0 <= request.g <= 255 and 0 <= request.b <= 255):
        raise HTTPException(status_code=400, detail="RGB values must be between 0 and 255.")
        
    try:
        send_ite_rgb_payload(request.brightness, request.effect, request.r, request.g, request.b)
        return {"status": "success", "effect": request.effect, "rgb": [request.r, request.g, request.b]}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

if __name__ == "__main__":
    print("LOQ Backend ready on Port 8000.")
    uvicorn.run(app, host="127.0.0.1", port=8000)