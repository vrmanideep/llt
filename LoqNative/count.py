user_input = input("Enter numbers: ")

# Split the string by commas and calculate the length of the resulting list
count = len(user_input.split(","))

print(f"Total numbers: {count}")

#input: 3, 4, 10, 11, 13, 15, 18, 20, 22, 23, 24, 26, 28, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 42, 43, 44, 45, 46, 47, 48, 50, 51, 53, 54, 57, 58, 61, 63, 65, 69, 70, 71, 73, 75, 76, 77, 79, 84, 85, 88, 90, 92, 93, 98, 99, 100, 101, 102, 103, 105, 112, 117, 118, 119, 120, 122, 123, 124, 126, 135, 136, 138, 141, 142, 143
#output: Total numbers: 75