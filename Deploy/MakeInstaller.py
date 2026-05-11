import os, argparse
from DeployUtils import appveyor_branch, fatal_error, exit_with_message, should_deploy, env, console, is_appveyor_build
from GenerateInstallerFileList import create_installer_files_list

###
# Example usages:
# Generate a zip patch:
#   py -3 Deploy/MakeInstaller.py --root_dir=. --patch --type=zip
# Generate a NSIS installer:
#   py -3 Deploy/MakeInstaller.py --root_dir=. --patch --type=nsis

parser = argparse.ArgumentParser()
parser.add_argument('--root_dir', type=str, help='BlackBox/ repository root directory', required=True)
parser.add_argument('--major', action='store_true', help='Is this a major release?')
parser.add_argument('--patch', action='store_true', help='Is this a cumulative patch?')
parser.add_argument('--type', type=str, help='Type of installer: nsis, zip, msi', default='nsis')
args = parser.parse_args()

BUILD_VERSION = env('APPVEYOR_BUILD_VERSION', default='1.60.00000')

os.chdir(args.root_dir)

# if this is a remote build on AppVeyor CI, then only create installer for specific branches
if is_appveyor_build() and not should_deploy():
    exit_with_message(f'Not creating installer for this branch: {appveyor_branch()}')

if   args.major: create_installer_files_list(major=True, type=args.type)
elif args.patch: create_installer_files_list(patch=True, type=args.type)

# create the upload dir if it doesn't already exist
source = os.getcwd()
if not os.path.exists('Deploy/upload'):
    os.makedirs('Deploy/upload', exist_ok=True)

if args.type == 'nsis':
    # Require MakeInstaller.py to have working directory in `BlackBox/` root
    makensis = os.path.abspath('Deploy/NSIS/makensis.exe')
    if not os.path.exists(makensis):
        fatal_error('makensis.exe was not found: MakeInstaller.py must be executed with WorkingDir=BlackBox/')

    installer = 'Deploy/BlackBox-Jupiter.nsi'
    if args.patch: installer = 'Deploy/BlackBox-Jupiter-Patch.nsi'

    console(f'\nMakeNSIS {installer}')
    result = os.system(f'"{makensis}" /V3 /DVERSION={BUILD_VERSION} /DSOURCE_DIR={source} {installer}')
    if result != 0: fatal_error(f'MakeNSIS returned with error: {result}')
    else: exit_with_message('MakeNSIS succeeded')
elif args.type == 'zip':
    zip7 = os.path.abspath('Deploy/7-Zip/7za.exe')
    if not os.path.exists(zip7):
        fatal_error('7za.exe was not found: MakeInstaller.py must be executed with WorkingDir=BlackBox/')

    installer = 'Deploy\\GeneratedFilesList.txt'
    archive_filename = f'BlackBox_Jupiter_{BUILD_VERSION}.zip'
    archive = f'Deploy\\upload\\{archive_filename}'    
    console(f'\nMakeZIP {installer}')
    result = os.system(f'cd game && "{zip7}" a -tzip ..\\{archive} @..\\{installer}')
    if result != 0: fatal_error(f'7zip returned with error: {result}')
    else: 
        # GitHub's release-asset cap is 2 GB; pick a value just under it. Historically
        # this was 25 MB to dodge an upload-tool limit on the AppVeyor/web-UI path
        # (Mars 1.51, commit 82358093b). Verified May 2026 against softprops/action-gh-release@v2
        # and `gh release create` (both hit uploads.github.com): a 115 MB asset uploads
        # cleanly in one shot. Chunker now only fires for genuine 2 GB+ patches.
        # AutoPatcher.PostProcessMultipleZipChunks short-circuits when Count == 1.
        max_size = 1900 * 1024 * 1024  # 1.9 GB
        if os.path.getsize(archive) > max_size:
            console(f'Archive is over {max_size // (1024*1024)}MB, splitting: {archive}')
            output_dir = os.path.dirname(archive)
            
            with open(archive, 'rb') as f:
                i = 1
                while True:
                    chunk = f.read(max_size)
                    if not chunk:
                        break
                        
                    part_name = f'{i:03d}-{archive_filename}'
                    part_path = os.path.join(output_dir, part_name)
                    with open(part_path, 'wb') as part_file:
                        part_file.write(chunk)
                    console(f'Created {part_name}')
                    i += 1
            
            os.remove(archive)
            console(f'Removed original large archive: {archive}')
        exit_with_message('7zip succeeded')
