<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Buku extends Model
{
    protected $table = 'buku';

    protected $fillable = [
        'judul',
        'pengarang',
        'penerbit',
        'stok',
    ];

    // Relasi ke peminjaman buku
    public function peminjamanBuku()
    {
        return $this->hasMany(PeminjamanBuku::class, 'buku_id');
    }
}
