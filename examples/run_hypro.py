import subprocess
from subprocess import TimeoutExpired
import os
import sys
import time

def system_call(cmd : str, timeout_sec=None):
    
    proc = subprocess.Popen(cmd, shell=True, stderr=subprocess.PIPE, stdout=subprocess.PIPE)

    try:
        stdout, stderr = proc.communicate(timeout=timeout_sec)
    except TimeoutExpired:
        proc.kill()
        return None, "", ""
   
    return proc.returncode, stdout.decode("utf-8").strip(), stderr.decode("utf-8").strip()

def call_hypro(system_path : str, formula_path : str, timeout : int):
    args = [
        '../app/HyPro',
        '--bp',
        system_path, 
        formula_path
    ]

    startTime = time.time()
    (code, out, err) = system_call(' '.join(args), timeout_sec=timeout)
    endTime = time.time()
    et = endTime - startTime 

    if code == None:
        return None

    if code != 0 or err != "": 
        print ("Error by HyPro: ", err, out, err, file=sys.stderr)
        return None
    
    if 'UNSAT' in out:
        res = False 
    else:
        assert ('SAT' in out)
        res = True
    
    return {'time': et, 'res': res}


instances = [
    {
        'bitwidth': 1, 
        'instances':
            [
                'gni/p1_1bit.txt',
                'gni/p2_1bit.txt',
                'gni/p3_1bit.txt',
                'gni/p4_1bit.txt',
            ]
    },
    {
        'bitwidth': 2, 
        'instances':
            [
                'gni/p1_2bit.txt',
                'gni/p2_2bit.txt',
                'gni/p3_2bit.txt',
                'gni/p4_2bit.txt',
            ]
    },
    {
        'bitwidth': 3, 
        'instances':
            [
                'gni/p1_3bit.txt',
                'gni/p2_3bit.txt',
                'gni/p3_3bit.txt',
                'gni/p4_3bit.txt',
            ]
    },
    {
        'bitwidth': 4, 
        'instances':
            [
                'gni/p1_4bit.txt',
                'gni/p2_4bit.txt',
                'gni/p3_4bit.txt',
                'gni/p4_4bit.txt',
            ]
    },
    {
        'bitwidth': 5, 
        'instances':
            [
                'gni/p1_5bit.txt',
                'gni/p2_5bit.txt',
                'gni/p3_5bit.txt',
                'gni/p4_5bit.txt',
            ]
    },
    {
        'bitwidth': 6, 
        'instances':
            [
                'gni/p1_6bit.txt',
                'gni/p2_6bit.txt',
                'gni/p3_6bit.txt',
                'gni/p4_6bit.txt',
            ]
    }
]

for bucket in instances:
    print('========================================================')
    print('Bitwidth:', bucket['bitwidth'])

    for path in bucket['instances']:
        
        res = call_hypro(system_path=path, formula_path='gni/gni.txt', timeout=180)

        if res == None:
            print('TO')
        else:
            print('Time: ', res['time'], 'seconds')
    print('========================================================\n')
