# config/

Holds runtime files that shouldn't be checked in - currently just `yahoo-token.json`, the saved Yahoo OAuth
access/refresh token pair written after you log in at http://localhost:5055/setup/yahoo (see the "Yahoo
leagues" section of the top-level README).

Everything in this folder except this file is git-ignored. Deleting `yahoo-token.json` (or using the
"Log out" button on the setup page) forces a fresh Yahoo login next time the app polls a Yahoo league.
