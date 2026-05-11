Unicode True
SetCompress auto
;SetCompressor /FINAL /SOLID bzip2
SetCompressor /FINAL /SOLID lzma
;SetCompressorDictSize 25 ; LZMA dict size, default is 8MB
CRCCheck force

; This script is intended to be run with WorkingDir=C:\Projects\BlackBox
; Written by RedFox
!ifndef VERSION
  !error "Missing required Script argument VERSION. Pass it via /DVERSION=x.x.x to makensis.exe"
!endif
!ifndef SOURCE_DIR
  !error "Missing required Script argument SOURCE_DIR. Pass it via /DSOURCE_DIR=C:\Projects\BlackBox to makensis.exe"
!endif

!define PRODUCT_NAME     "StarDrive BlackBox Jupiter Patch"
!define INSTALLER_NAME   "BlackBox_Jupiter_Patch"
!define PRODUCT_VERSION  ${VERSION}

; Patch-only flag. BBInstaller.nsi reads this in .onInit to abort when no
; existing Jupiter install is present — patches are incremental deltas
; against the major's Release.txt baseline, never standalone, and overlaying
; one on a vanilla/Steam folder produces a frankenstein install (only the
; files that changed since the baseline are shipped; the rest stays vanilla).
!define IS_PATCH

;; Payload:
!include "BBInstaller.nsi"
