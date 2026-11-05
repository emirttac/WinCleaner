# MicroWin spike

This note does not add code. It is not a release requirement.

## 9a — answer file only

Generate `autounattend.xml` on disk. Do not mount an image, do not call DISM against a WIM, and do not change the running system. The file is a text artifact the user can inspect. Risk is low. If it is built later, it is a separate effort, not part of the maturity exit.

## 9b — offline image

Official ISO check, DISM WIM mount, offline AppX and feature changes, offline registry hives, driver injection, `oscdimg`, and USB writing. Do not write back onto the original ISO. Effort is above the current phases, and a bad mount can destroy data.

Do not build 9b. The maturity work through restore points, gates, and catalogs has to stay green, and 9a would need to exist for a release, before any offline image work is reconsidered.
