<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class PeminjamanRuangan extends Model
{
    protected $table = 'peminjaman_ruangan';

    protected $fillable = [
        'user_id',
        'ruangan_id',
        'tanggal_mulai',
        'tanggal_selesai',
        'status',
    ];

    // Relasi ke user
    public function user()
    {
        return $this->belongsTo(User::class, 'user_id');
    }

    // Relasi ke ruangan
    public function ruangan()
    {
        return $this->belongsTo(Ruangan::class, 'ruangan_id');
    }
}
