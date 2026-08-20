# Generate test CSV files with real GS bytes (0x1D) for printer testing

GS = '\x1d'

# Test 3: Real GS codes, WITH "QR" header
codes_with_header = [
    "QR",
    f"0104850006070011211AAAAAA{GS}93AA01",
    f"0104850006070011211BBBBBB{GS}93BB02",
    f"0104850006070011211CCCCCC{GS}93CC03",
]

with open('demo/test_gs_with_header.csv', 'w', newline='') as f:
    for code in codes_with_header:
        f.write(code + '\n')

# Test 4: Real GS codes, WITHOUT header (current behavior)
codes_no_header = codes_with_header[1:]  # skip "QR"

with open('demo/test_gs_no_header.csv', 'w', newline='') as f:
    for code in codes_no_header:
        f.write(code + '\n')

# Verify
for name in ['demo/test_gs_with_header.csv', 'demo/test_gs_no_header.csv']:
    with open(name, 'rb') as f:
        data = f.read()
    gs_count = data.count(b'\x1d')
    lines = data.strip().split(b'\n')
    print(f"{name}: {len(lines)} lines, {gs_count} GS bytes")
    for i, line in enumerate(lines):
        ascii_repr = ''.join(chr(b) if 32 <= b < 127 else f'[{b:02x}]' for b in line)
        print(f"  Line {i}: {ascii_repr}")

print()
print("Test plan:")
print("  Test 1: test_no_gs_no_header.csv  -> Import, print 3. If 3 unique = GS is the problem")
print("  Test 2: test_no_gs_with_header.csv -> Import, print 3. If 3 unique = header doesn't matter")
print("  Test 3: test_gs_no_header.csv      -> Import, print 3. If 2 unique = GS confirmed")
print("  Test 4: test_gs_with_header.csv    -> Import, print 3. If 3 unique = both GS+header needed")
