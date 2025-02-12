# Kingdom Come Auto Packer

This program simplifies the workflow for Kingdom Come modmakers. Forget manually packing your .pak files after every change – just work in a `.unpacked` folder, and your changes are automatically synced to the adjacent `.pak` file.

## How It Works

- **Automatic Sync:** The program watches your specified directory (or defaults to the current folder) and all its subdirectories.
- **Folder Detection:** When it finds a folder ending in `.unpacked`, it automatically creates or updates a `.pak` file right next to it.
- **Smart Updates:** Files are added, updated, or removed in the archive based on their name, size, and last modification date (note: modification dates are tracked with a precision of 2 seconds due to technical limitations).

## Usage

- **Drop it in `/Mods`:** Simply place the executable in your `/Mods` folder and run.
- **Command-line Argument:** You can also run the program with a path argument pointing to the `/Mods` folder or any of its subfolders.

## WARNING

If you create an empty `.unpacked` folder next to an existing `.pak` archive, it will delete its contents. If you want to work with an existing `.pak` file, unpack it completely first and rename the folder to `.unpacked`