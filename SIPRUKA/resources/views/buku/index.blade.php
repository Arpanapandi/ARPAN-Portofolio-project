<x-app-layout>
    <x-slot name="header">
        <h2 class="font-semibold text-xl">Daftar Buku</h2>
    </x-slot>

    <div class="p-6">
        <a href="{{ route('buku.create') }}" 
           class="bg-gray-200 text-black px-4 py-2 rounded hover:bg-gray-300">
            Tambah Buku
        </a>

        <table class="mt-4 w-full border">
            <tr>
                <th class="border p-2 w-12">No</th>
                <th class="border p-2">Judul</th>
                <th class="border p-2">Penulis</th>
                <th class="border p-2">Penerbit</th>
                <th class="border p-2">Stok</th>
                <th class="border p-2 w-32">Aksi</th>
            </tr>

            @foreach($buku as $b)
            <tr>
                <td class="border p-2 text-center">{{ $loop->iteration }}</td>
                <td class="border p-2">{{ $b->judul }}</td>
                <td class="border p-2">{{ $b->pengarang }}</td>
                <td class="border p-2">{{ $b->penerbit }}</td>
                <td class="border p-2">{{ $b->stok }}</td>

                <td class="border p-2 text-center">
                    <a href="{{ route('buku.edit', $b->id) }}" class="text-blue-600">Edit</a>

                    <form action="{{ route('buku.destroy', $b->id) }}" method="POST" class="inline">
                        @csrf
                        @method('DELETE')
                        <button class="text-red-600" 
                            onclick="return confirm('Yakin ingin menghapus buku ini?')">
                            Hapus
                        </button>
                    </form>
                </td>
            </tr>
            @endforeach

        </table>
    </div>
</x-app-layout>
