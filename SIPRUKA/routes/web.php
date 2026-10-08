<?php

use App\Http\Controllers\ProfileController;
use Illuminate\Support\Facades\Route;

Route::get('/', function () {
    return view('welcome');
});

Route::get('/dashboard', function () {
    return view('dashboard');
})->middleware(['auth', 'verified'])->name('dashboard');

Route::middleware('auth')->group(function () {
    Route::get('/profile', [ProfileController::class, 'edit'])->name('profile.edit');
    Route::patch('/profile', [ProfileController::class, 'update'])->name('profile.update');
    Route::delete('/profile', [ProfileController::class, 'destroy'])->name('profile.destroy');
});

require __DIR__.'/auth.php';

use App\Http\Controllers\BukuController;
use App\Http\Controllers\RuanganController;
use App\Http\Controllers\PeminjamanBukuController;
use App\Http\Controllers\PeminjamanRuanganController;

Route::middleware('auth')->group(function () {
    Route::resource('buku', BukuController::class);
    Route::resource('ruangan', RuanganController::class);
    Route::resource('peminjaman-buku', PeminjamanBukuController::class);
    Route::resource('peminjaman-ruangan', PeminjamanRuanganController::class);
});
