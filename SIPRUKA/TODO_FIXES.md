# TODO: Fix Inconsistencies in Laravel Library System

## Current Issues Identified
- [ ] User model fillable fields don't match migration (migration has 'nama', model has 'name')
- [ ] Missing routes for BukuController and RuanganController
- [ ] PeminjamanBukuController uses 'peminjam' field but model uses 'user_id'
- [ ] Inconsistent view layouts (some use x-app-layout, some use custom Bootstrap)
- [ ] Navigation in app.blade.php references non-existent routes
- [ ] Views use wrong field names for users ('nama' vs 'name')

## Tasks
- [ ] Fix User model fillable fields
- [ ] Add missing routes for Buku and Ruangan controllers
- [ ] Update PeminjamanBukuController to use user_id properly
- [ ] Update peminjaman_buku views to use consistent layout and correct fields
- [ ] Update peminjaman_ruangan views to use consistent layout
- [ ] Fix navigation links in app.blade.php
- [ ] Test the application functionality

## Files to Edit
- app/Models/User.php
- routes/web.php
- app/Http/Controllers/PeminjamanBukuController.php
- resources/views/peminjaman_buku/index.blade.php
- resources/views/peminjaman_buku/create.blade.php
- resources/views/peminjaman_buku/edit.blade.php
- resources/views/peminjaman_ruangan/index.blade.php
- resources/views/peminjaman_ruangan/create.blade.php
- resources/views/peminjaman_ruangan/edit.blade.php
- resources/views/layouts/app.blade.php
