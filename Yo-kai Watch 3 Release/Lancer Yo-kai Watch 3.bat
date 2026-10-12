@echo off
title Yo-kai Watch 3 - Portage Natif PC (Double Ecran Haut / Bas)
chcp 65001 >nul
echo ===============================================================================
echo            PORTAGE NATIF PC - YO-KAI WATCH 3 (FRANCE)
echo                  AUTHENTIQUE DOUBLE ECRAN (HAUT ^& BAS)
echo ===============================================================================
echo.
echo  [DISPOSITION DU JEU]
echo    - Ecran du haut (3D)  : Affiche l'aventure, cinematiques, ville, combats
echo    - Ecran du bas (Touch): Radar Yo-kai, menus, inventaire, mini-jeux tactiles
echo.
echo  [COMMANDES ^& RACCOURCIS]
echo    - Clic Gauche Souris  : Utilisation directe de l'ecran tactile du bas
echo    - F11                 : Plein ecran / Fenetre
echo    - F9                  : Changer disposition (Superpose Haut/Bas ou Cote a Cote)
echo    - F10                 : Alterner Ecran Haut / Ecran Bas
echo    - F1                  : Mettre en pause / Reprendre
echo    - Echap (Esc)         : Menu Zakuro / Pause (Settings, Volume, etc.)
echo.
echo ===============================================================================
echo Lancement du jeu en Double Ecran natif...
echo ===============================================================================
echo.

set YW3_DUAL_SCREEN=1
set NO_DIRECTOR=1

cd /d "%~dp0"
"yokaiwatch3.exe" "%~dp0Yo-Kai Watch 3 (France).3ds" --dual-screen --fullscreen --data "%~dp0saves"
exit
