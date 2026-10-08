<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class PeminjamanBuku extends Model
{
    protected $table = 'peminjaman_buku';

    protected $fillable = [
        'user_id',
        'buku_id',
        'tanggal_pinjam',
        'tanggal_kembali',
        'status',
    ];

    // Relasi ke user
    public function user()
    {
        return $this->belongsTo(User::class, 'user_id');
    }

    // Relasi ke buku
    public function buku()
    {
        return $this->belongsTo(Buku::class, 'buku_id');
    }
}
